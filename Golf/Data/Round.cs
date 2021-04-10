using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Golf.Data.Eventuous;
using ValueOf;

namespace Golf.Data
{
    public static class RepositoryExtensions
    {
        public static async Task<T> Dispatch<T, TCommand>(this IRepository repository, TCommand c) 
            where T : Aggregate, IHandle<TCommand>, new()
            where TCommand : Command
        {
            Console.WriteLine($"Processing {typeof(TCommand).Name}");
            var aggregate = await repository.Get<T>(c.Identifier);
            var events = aggregate.When(c);
            var enumerable = events.ToList();
            await repository.Save(c.Identifier, aggregate.Version, enumerable);
            Console.WriteLine($"Processed {typeof(TCommand).Name}");
            aggregate.Given(enumerable);
            return aggregate;
        }
    }

    public interface IRepository
    {
        Task<T> Get<T>(string id) where T : Aggregate, new();
        Task Save(string id, int version, IEnumerable<Event> events);
    }

    public class Repository : IRepository
    {
        private readonly Dictionary<string, List<Event>> _store = new();

        public Task<T> Get<T>(string id) where T : Aggregate, new()
        {
            List<Event> events;
            lock (_store)
            {
                if (!_store.TryGetValue(id, out events))
                {
                    events = new List<Event>();
                }
            }

            var aggregate = new T();
            aggregate.Given(events);

            return Task.FromResult(aggregate);
        }


        public Task Save(string id, int version, IEnumerable<Event> newEvents)
        {
            lock (_store)
            {
                var events = newEvents.ToList();
                if (!_store.TryGetValue(id, out var stream))
                {
                    HandleNoStream(id, version, events);

                    return Task.CompletedTask;
                }

                VerifyExpectedVersion(version, stream);

                stream.AddRange(events);

                return Task.CompletedTask;
            }
        }

        private static void VerifyExpectedVersion(int version, List<Event> stream)
        {
            if (stream.Count - 1 != version)
            {
                throw new WrongVersionException(
                    $"Expected stream version {version} but found version {stream.Count - 1}");
            }
        }

        private void HandleNoStream(string id, int version, List<Event> events)
        {
            _store[id] = version == -1
                ? events
                : throw new WrongVersionException("Expected no stream but found one.");
        }
    }

    public record Course(string Name, Guid Id)
    {
        public static readonly Course[] Courses = {
            new("Centerpointe", Guid.Parse("e0b656e8-7d95-4a36-b384-032e8169a037"))
        };
    }

    public class RoundViewModel : IRoundViewModel
    {
        private readonly RoundService _svc;
        private GolfRoundState _state;
        public GolfRoundState Round => _state;
        public string SelectedCourse { get; set; }
        private readonly RoundId _roundId = new RoundId("the-round");
        public RoundViewModel(RoundService svc)
        {
            _svc = svc;
        }

        public string SelectedCourseName => string.IsNullOrWhiteSpace(SelectedCourse)
            ? string.Empty
            : Course.Courses.First(c => c.Id.ToString() == SelectedCourse).Name;
        public async Task CreateRound()
        {
            var result = await _svc.Handle(new SelectCourse(_roundId,
                CourseId.From(Guid.Parse(SelectedCourse))));
            _state = result.State;
        }

        public async Task SelectGolfer()
        {
            var result = await _svc.Handle(new SelectGolfer(_roundId,
                GolferId.From(Guid.NewGuid()), true));
            _state = result.State;
        }
    }

    public interface IRoundViewModel
    {
        GolfRoundState Round { get; }
        string SelectedCourse { get; set; }
        string SelectedCourseName { get; }
        Task CreateRound();
        Task SelectGolfer();
    }

    public class WrongVersionException : Exception
    {
        public WrongVersionException(string s) : base(s)
        {
        }
    }

    public record Command(string Identifier);

    public interface IHandle<in T>
    {
        IEnumerable<Event> When(T command);
    }

    public record Event;

    public record RoundState
    {
        public string RoundId { get; init; }
        public Guid CourseId { get; init; }
        public HashSet<Guid> GolferIds { get; init; } = new();
        public Dictionary<(Guid golfer, int hole), int> Scores { get; init; } = new();
    }

    public class Round : Aggregate,
        IHandle<SelectCourse>,
        IHandle<SelectGolfer>,
        IHandle<SubmitHoleScore>
    {
        public RoundState State { get; private set; } = new();

        public override string Id { get; protected set; }

        protected override void Given(Event @event)
        {
            switch (@event)
            {
                case CourseSelected c:
                    State = State with {CourseId = c.CourseId, RoundId = c.RoundId};
                    Id = c.RoundId;
                    break;
                case GolferSelected g:
                    var golferIds = State.GolferIds;
                    golferIds.Add(g.GolferId);
                    State = State with {GolferIds = golferIds};
                    break;
                case HoleScoreSubmitted h:
                    var scores = State.Scores;
                    scores.Add((h.GolferId, h.HoleNumber), h.Strokes);
                    State = State with {Scores = scores};
                    break;
            }
        }

        public IEnumerable<Event> When(SelectCourse selectCourse)
        {
            var (roundId, courseId) = selectCourse;
            yield return new CourseSelected(roundId, courseId);
        }

        public IEnumerable<Event> When(SelectGolfer selectGolfer)
        {
            var (roundId, golferId, generateScores) = selectGolfer;
            if (State.GolferIds.Contains(golferId))
            {
                yield break;
            }

            yield return new GolferSelected(roundId, GolferId.From(golferId));
            if (!generateScores) yield break;
            for (int i = 0; i < 9; i++)
            {
                yield return new HoleScoreSubmitted(roundId, golferId, i + 1, new Random().Next(2, 8));
            }
        }

        public IEnumerable<Event> When(SubmitHoleScore submitHoleScore)
        {
            var (roundId, holeScore, golferId) = submitHoleScore;
            yield return new HoleScoreSubmitted(
                roundId,
                golferId,
                holeScore.Value.Hole,
                holeScore.Value.Score);
        }
    }

    public record HoleScoreSubmitted(
        string RoundId, 
        Guid GolferId, 
        int HoleNumber, 
        int Strokes) : Event;

    public record GolferSelected(string RoundId, Guid GolferId) : Event;

    public abstract class Aggregate
    {
        public int Version { get; private set; } = -1;

        public void Given(IEnumerable<Event> events)
        {
            foreach (var @event in events)
            {
                Given(@event);
                Version++;
            }
        }

        protected abstract void Given(Event @event);
        public abstract string Id { get; protected set; }
    }

    public record SelectCourse(RoundId RoundId, CourseId CourseId) : Command(RoundId);

    public record CourseSelected(string RoundId, Guid CourseId) : Event;

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
            if (Value.Hole < 0 || Value.Hole > 18) throw new ArgumentOutOfRangeException(nameof(Value.Hole));
            if (Value.Score < 1) throw new ArgumentOutOfRangeException(nameof(Value.Hole));
        }
    }

    public record SubmitHoleScore(RoundId RoundId, HoleScore Score, GolferId GolferId) : Command(RoundId);

    public record SelectGolfer(RoundId RoundId, GolferId GolferId, bool GenerateScores) : Command(RoundId);
}