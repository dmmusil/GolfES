using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Eventuous;

namespace Golf.Data.Eventuous
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
}