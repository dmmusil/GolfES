using System;
using System.Collections.Generic;
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
        public RoundService(IAggregateStore store) : base(store)
        {
            OnNew<SelectCourse>((round, select) =>
                round.SelectCourse(select.RoundId, select.CourseId));
            OnExisting<SelectGolfer>(
                select => select.RoundId,
                (round, golfer) => round.SelectGolfer(golfer.RoundId, golfer.GolferId, true)
            );
            OnExisting<SubmitHoleScore>(
                cmd => cmd.RoundId,
                (round, hole) => round.SubmitHoleScore(hole.RoundId, hole.Score, hole.GolferId)
            );
        }
    }

    public class GolfRound : Aggregate<GolfRoundState, RoundId>
    {
        public void SelectCourse(RoundId roundId, CourseId courseId)
        {
            EnsureDoesntExist();
            Apply(new CourseSelected(roundId, courseId));
        }

        public void SelectGolfer(RoundId roundId, GolferId golferId, bool generateScores)
        {
            if (State.GolferIds.Contains(golferId)) return;

            Apply(new GolferSelected(roundId, golferId));

            if (!generateScores) return;

            for (var i = 0; i < 9; i++)
            {
                Apply(new HoleScoreSubmitted(roundId, golferId, i + 1, new Random().Next(2, 8)));
            }
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
                CourseSelected c => this with {Id = new RoundId(c.RoundId), CourseId = c.CourseId, RoundId = c.RoundId},
                GolferSelected g => GolferSelected(this, g),
                HoleScoreSubmitted h => HoleScoreSubmitted(this, h),
                _ => throw new ArgumentOutOfRangeException(nameof(@event), "Unknown event")
            };

            static GolfRoundState GolferSelected(GolfRoundState state, GolferSelected g)
            {
                var golfers = state.GolferIds;
                golfers.Add(g.GolferId);
                return state with {GolferIds = golfers};
            }

            static GolfRoundState HoleScoreSubmitted(GolfRoundState state, HoleScoreSubmitted h)
            {
                var holes = state.Scores;
                holes.Add((h.GolferId, h.HoleNumber), h.Strokes);
                return state with {Scores = holes};
            }
        }

        public Dictionary<(Guid golfer, int hole), int> Scores { get; init; } = new();

        public HashSet<Guid> GolferIds { get; init; } = new();

        public Guid CourseId { get; init; }

        public string RoundId { get; init; }
    }
}