using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Eventuous;
using MongoDB.Driver;
using ValueOf;

namespace Golf.Data
{
    public static class RoundEvents
    {
        public static void Register()
        {
            TypeMap.AddType<CourseSelected>(nameof(CourseSelected));
            TypeMap.AddType<GolferSelected>(nameof(GolferSelected));
            TypeMap.AddType<HoleScoreSubmitted>(nameof(HoleScoreSubmitted));
        }
    }

    public class RoundService : ApplicationService<GolfRound, GolfRoundState, RoundId>
    {
        private readonly IAggregateStore _store;

        public RoundService(IAggregateStore store) : base(store)
        {
            _store = store;
            OnAny<SelectCourse>(
                cmd => cmd.RoundId,
                (round, select) => round.SelectCourse(select.RoundId, select.CourseId));

            OnExisting<SelectGolfer>(
                select => select.RoundId,
                (round, golfer) => round.SelectGolfer(golfer.RoundId, golfer.GolferId, true)
            );

            OnExisting<SubmitHoleScore>(
                cmd => cmd.RoundId,
                (round, hole) => round.SubmitHoleScore(hole.RoundId, hole.Score, hole.GolferId)
            );
        }

        public async Task<GolfRoundState> LoadState(string roundId)
        {
            return (await _store.Load<GolfRound>(roundId)).State;
        }
    }

    public class GolfRound : Aggregate<GolfRoundState, RoundId>
    {
        public void SelectCourse(RoundId roundId, CourseId courseId)
        {
            Apply(new CourseSelected(roundId, courseId));
        }

        public void SelectGolfer(RoundId roundId, GolferId golferId, bool generateScores)
        {
            if (State.GolferIds.Contains(golferId)) return;

            Apply(new GolferSelected(roundId, golferId));
        }

        public void SubmitHoleScore(RoundId roundId, HoleScore score, GolferId golferId)
        {
            Apply(new HoleScoreSubmitted(roundId, golferId, score.Value.Hole, score.Value.Score));
        }
    }

    public record RoundId(string Value) : AggregateId(Value);

    public record GolfRoundState : AggregateState<GolfRoundState, RoundId>
    {
        public override GolfRoundState When(object @event)
        {
            return @event switch
            {
                CourseSelected c => this with
                {
                    Id = new RoundId(c.RoundId), 
                    CourseId = c.CourseId, 
                    RoundId = c.RoundId
                },
                GolferSelected g => this with
                {
                    GolferIds = GolferIds.Add(g.GolferId)
                },
                HoleScoreSubmitted h => this with
                {
                    Scores = AddScore(Scores, h)
                },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(@event), 
                    "Unknown event")
            };
       }

        private static Dictionary<Guid, Dictionary<int, int>> AddScore(Dictionary<Guid,Dictionary<int,int>> scores, HoleScoreSubmitted h)
        {
            if (!scores.TryGetValue(h.GolferId, out var holes))
            {
                holes = scores[h.GolferId] = new Dictionary<int, int>();
            }
            
            holes[h.HoleNumber] = h.Strokes;
            return scores;
        }

        public Dictionary<Guid, Dictionary<int, int>> Scores { get; init; } = new();

        public ImmutableHashSet<Guid> GolferIds { get; init; } = ImmutableHashSet<Guid>.Empty;

        public Guid CourseId { get; init; }

        public string RoundId { get; init; }
    }
    
    public record Course(string Name, Guid Id)
    {
        public static readonly Course[] Courses =
        {
            new("Centerpointe", Guid.Parse("e0b656e8-7d95-4a36-b384-032e8169a037"))
        };
    }

    public class RoundViewModel : IRoundViewModel
    {
        private readonly RoundService _svc;
        private readonly IMongoDatabase _db;
        private GolfRoundState _state;
        public GolfRoundState Round => _state;
        public string SelectedCourse { get; set; }
        private RoundId RoundId => _state.Id;

        public RoundViewModel(RoundService svc, IMongoDatabase db)
        {
            _svc = svc;
            _db = db;
        }

        public string SelectedCourseName => string.IsNullOrWhiteSpace(SelectedCourse)
            ? string.Empty
            : Course.Courses.First(c => c.Id.ToString() == SelectedCourse).Name;

        public async Task CreateRound(string id)
        {
            var result = await _svc.Handle(new SelectCourse(new RoundId(id),
                CourseId.From(Guid.Parse(SelectedCourse))));
            _state = result.State;
        }

        public async Task SelectGolfer()
        {
            var result = await _svc.Handle(new SelectGolfer(RoundId,
                GolferId.From(Guid.NewGuid()), true));
            _state = result.State;
        }

        public async Task Refresh()
        {
            var findAsync = await _db.GetCollection<Stats>("Stats").FindAsync(s => s.Id == RoundId);
            var stats = findAsync.Single();
            Strokes = stats.Strokes;
        }

        public int Strokes { get; private set; }
        public IEnumerable<Guid> Players => Round.Scores.Keys;

        public async Task Load(string roundId)
        {
            try
            {
                _state = await _svc.LoadState(roundId);
            }
            catch
            {
                _state = null;
            }
        }
    }

    public interface IRoundViewModel
    {
        GolfRoundState Round { get; }
        string SelectedCourse { get; set; }
        string SelectedCourseName { get; }
        Task CreateRound(string id);
        Task SelectGolfer();
        Task Refresh();
        int Strokes { get; }
        IEnumerable<Guid> Players { get; }
        Task Load(string roundId);
    }


    public record HoleScoreSubmitted(
        string RoundId,
        Guid GolferId,
        int HoleNumber,
        int Strokes);

    public record GolferSelected(string RoundId, Guid GolferId);

    public record SelectCourse(RoundId RoundId, CourseId CourseId);

    public record CourseSelected(string RoundId, Guid CourseId);

    public class CourseId : ValueOf<Guid, CourseId>
    {
        protected override void Validate()
        {
            if (Value == Guid.Empty) throw new ArgumentException(nameof(Value));
        }
    }

    public class GolferId : ValueOf<Guid, GolferId>
    {
        protected override void Validate()
        {
            if (Value == Guid.Empty) throw new ArgumentException(nameof(Value));
        }
    }

    public class HoleScore : ValueOf<(int Hole, int Score), HoleScore>
    {
        protected override void Validate()
        {
            if (Value.Hole is < 0 or > 18) throw new ArgumentOutOfRangeException(nameof(Value.Hole));
            if (Value.Score < 1) throw new ArgumentOutOfRangeException(nameof(Value.Hole));
        }
    }

    public record SubmitHoleScore(RoundId RoundId, HoleScore Score, GolferId GolferId);

    public record SelectGolfer(RoundId RoundId, GolferId GolferId, bool GenerateScores);
}