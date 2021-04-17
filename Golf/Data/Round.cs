using System;
using System.Linq;
using System.Threading.Tasks;
using Golf.Data.Eventuous;
using MongoDB.Driver;
using ValueOf;

namespace Golf.Data
{
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