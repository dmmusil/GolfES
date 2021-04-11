using System;
using System.Linq;
using System.Threading.Tasks;
using Golf.Data;
using Golf.Data.Eventuous;
using Xunit;

namespace Golf.Tests
{
    public class UnitTest1
    {
        private readonly RoundId RoundId = new("the-round");

        [Fact]
        public void Select_course()
        {
            var courseId = CourseId.From(Guid.NewGuid());
            var selectCourse = new SelectCourse(RoundId, courseId);

            var round = new Round();
            var then = round.When(selectCourse).ToList();
            Assert.Single(then);
            var selected = then[0] as CourseSelected;
            Assert.Equal(courseId.Value, selected?.CourseId);
        }

        [Fact]
        public async Task Select_course_repo()
        {
            var courseId = CourseId.From(Guid.NewGuid());
            var selectCourse = new SelectCourse(RoundId, courseId);

            var repository = new Repository();
            await repository.Dispatch<Round, SelectCourse>(selectCourse);
            var round = await repository.Get<Round>(RoundId);
            Assert.Equal(courseId, round.State.CourseId);
        }

        [Fact]
        public void Select_player()
        {
            var golferId = GolferId.From(Guid.NewGuid());
            var selectGolfer = new SelectGolfer(RoundId, golferId, false);

            var round = new Round();
            var then = round.When(selectGolfer).ToList();
            Assert.Single(then);
            var golfer = (GolferSelected) then[0];
            Assert.Equal(golferId, golfer.GolferId);
        }

        [Fact]
        public void Submit_hole()
        {
            var holeScore = HoleScore.From((12, 4));
            var golferId = GolferId.From(Guid.NewGuid());
            var submitHoleScore = new SubmitHoleScore(RoundId, holeScore, golferId);

            var round = new Round();
            var then = round.When(submitHoleScore).ToList();
            Assert.Single(then);
            var (_, golfer, holeNumber, strokes) = (HoleScoreSubmitted) then[0];
            Assert.Equal(holeScore.Value.Hole, holeNumber);
            Assert.Equal(holeScore.Value.Score, strokes);
            Assert.Equal(golferId, golfer);
        }

        [Fact]
        public async Task Submit_18_holes()
        {
            var golferId = GolferId.From(Guid.NewGuid());
            var repository = new Repository();
            for (var i = 1; i <= 18; i++)
            {
                var holeScore = HoleScore.From((i, 4));
                var submitHoleScore = new SubmitHoleScore(RoundId, holeScore, golferId);
                await repository.Dispatch<Round, SubmitHoleScore>(submitHoleScore);
            }

            var round = await repository.Get<Round>(RoundId);

            Assert.Equal(18, round.State.Scores.Count);
            Assert.Equal(72, round.State.Scores.Sum(s => s.Value));
        }
    }
}