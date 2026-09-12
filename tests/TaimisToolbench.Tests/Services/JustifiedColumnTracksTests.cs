using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class JustifiedColumnTracksTests
    {
        [Fact]
        public void AdjacentTracks_ShareAnEdgeExactly()
        {
            const int startX = 40;
            const int span = 1000;
            const int tracks = 6;

            for (int i = 0; i < tracks - 1; i++)
            {
                Assert.Equal(
                    JustifiedColumnTracks.RightEdge(startX, span, tracks, i),
                    JustifiedColumnTracks.LeftEdge(startX, span, tracks, i + 1));
            }
        }

        [Fact]
        public void TheLastTrack_EndsExactlyOnTheSpan_NotARoundingPixelShort()
        {
            // 1000 / 7 does not divide; accumulating a rounded track width
            // would strand pixels at the right edge.
            Assert.Equal(
                40 + 1000,
                JustifiedColumnTracks.RightEdge(40, 1000, 7, 6));
        }

        [Fact]
        public void TrackWidths_SumToTheWholeSpan()
        {
            int total = 0;
            for (int i = 0; i < 7; i++)
            {
                total += JustifiedColumnTracks.Width(40, 1000, 7, i);
            }

            Assert.Equal(1000, total);
        }

        [Fact]
        public void CenteredContent_SitsMidTrack()
        {
            // Track 1 of 4 over a 400px span from 0 spans 100..200.
            Assert.Equal(125, JustifiedColumnTracks.CenteredX(0, 400, 4, 1, 50));
        }

        [Fact]
        public void AHeaderAndItsCell_CentreOnTheSameAxis()
        {
            // The point of the law: a narrow header and a wide cell in the
            // same track share a centre line, so the header sits over the
            // values it names.
            int header = JustifiedColumnTracks.CenteredX(0, 400, 4, 2, 40);
            int cell = JustifiedColumnTracks.CenteredX(0, 400, 4, 2, 90);

            Assert.Equal(header + (40 / 2), cell + (90 / 2));
        }

        [Fact]
        public void ContentWiderThanItsTrack_PinsLeft_RatherThanOverhangingBothNeighbours()
        {
            Assert.Equal(
                JustifiedColumnTracks.LeftEdge(0, 400, 4, 1),
                JustifiedColumnTracks.CenteredX(0, 400, 4, 1, 500));
        }

        [Fact]
        public void ZeroTracks_DegradeToTheOrigin_RatherThanDividingByZero()
        {
            Assert.Equal(40, JustifiedColumnTracks.LeftEdge(40, 1000, 0, 0));
            Assert.Equal(40, JustifiedColumnTracks.RightEdge(40, 1000, 0, 0));
            Assert.Equal(0, JustifiedColumnTracks.Width(40, 1000, 0, 0));
            Assert.Equal(40, JustifiedColumnTracks.CenteredX(40, 1000, 0, 0, 10));
        }

        [Fact]
        public void FitsDistributed_IsFalse_WhenATrackCannotHoldItsWidestBandPlusTheGap()
        {
            Assert.True(JustifiedColumnTracks.FitsDistributed(600, 6, 80, 12));
            Assert.False(JustifiedColumnTracks.FitsDistributed(600, 6, 90, 12));
        }

        [Fact]
        public void CenteredInBand_IsTheSameLawAgainstOneColumnsOwnBand()
        {
            // The form every table's HEADER uses: the band, not an equal
            // share of the row, is what the word has to sit over.
            Assert.Equal(155, JustifiedColumnTracks.CenteredInBand(100, 150, 40));
            // A band is a one-track row: the two forms must not drift apart.
            Assert.Equal(
                JustifiedColumnTracks.CenteredX(100, 150, 1, 0, 40),
                JustifiedColumnTracks.CenteredInBand(100, 150, 40));
        }

        [Fact]
        public void CenteredInBand_ContentAtLeastAsWideAsTheBand_PinsLeft()
        {
            Assert.Equal(100, JustifiedColumnTracks.CenteredInBand(100, 150, 150));
            Assert.Equal(100, JustifiedColumnTracks.CenteredInBand(100, 150, 400));
            Assert.Equal(100, JustifiedColumnTracks.CenteredInBand(100, 0, 40));
        }

        // --- CenteredOverContent (the header law: the word sits over the
        // ink, and only a NEIGHBOURING column may stop it) ---
        private static JustifiedColumnTracks.HeaderRoom WideOpen(int contentX)
        {
            // Neighbours 1000px away on both sides: nothing to collide
            // with, so nothing may move the header off its ink.
            return JustifiedColumnTracks.HeaderRoom.Between(contentX - 1000, contentX + 1000);
        }

        [Fact]
        public void CenteredOverContent_LeftRuledColumn_IgnoresTheReserveThatContentDoesNotFill()
        {
            // A 150px band whose badges only ever reach 60px: the header
            // centres on the badges' own 100..160 extent, not on the band's
            // 100..250. Centring in the band would put it 45px right of the
            // ink, which is the defect this exists to fix.
            Assert.Equal(120, JustifiedColumnTracks.CenteredOverContent(100, 60, 20, WideOpen(100)));
            Assert.Equal(165, JustifiedColumnTracks.CenteredInBand(100, 150, 20));
        }

        [Fact]
        public void CenteredOverContent_HeaderWiderThanItsInk_StillCentresOnTheInk()
        {
            // THE regression. A "Have" column whose widest value is a
            // single "0" is ~8px of ink under a ~41px word. Nothing about
            // that narrowness may right-align the header: its CENTRE
            // belongs on the ink's centre, and the word overhangs the
            // column symmetrically to get there.
            const int contentX = 500;
            const int ink = 8;
            const int headerWidth = 42;

            int x = JustifiedColumnTracks.CenteredOverContent(
                contentX, ink, headerWidth, WideOpen(contentX));

            Assert.Equal(2 * contentX + ink, 2 * x + headerWidth);
            Assert.Equal(483, x);

            // What the band clamp used to answer: the header's right edge
            // pinned to the values' right edge, i.e. right-alignment.
            Assert.NotEqual(contentX + ink - headerWidth, x);
        }

        [Fact]
        public void CenteredOverContentRightAligned_HeaderWiderThanItsInk_StillCentresOnTheInk()
        {
            // The same column stated the way every numeric column states
            // it: cells grow leftward off a shared right edge.
            const int rightEdge = 508;
            const int ink = 8;
            const int headerWidth = 42;

            int x = JustifiedColumnTracks.CenteredOverContentRightAligned(
                rightEdge, ink, headerWidth, WideOpen(rightEdge - ink));

            Assert.Equal(2 * rightEdge - ink, 2 * x + headerWidth);
            Assert.NotEqual(rightEdge - headerWidth, x);
        }

        [Fact]
        public void CenteredOverContent_NoInkAtAll_CentresOnTheColumnsOwnRule()
        {
            // A column every row leaves blank has no extent to centre over,
            // so the header centres on the rule its cells would have used
            // rather than wandering to the middle of whatever room it has.
            Assert.Equal(80, JustifiedColumnTracks.CenteredOverContent(100, 0, 40, WideOpen(100)));
        }

        [Fact]
        public void CenteredOverContent_ContentWiderThanItsHeader_SitsInsideTheContent()
        {
            // The ordinary case: a 400px coin run under a 40px word.
            int x = JustifiedColumnTracks.CenteredOverContent(100, 400, 40, WideOpen(100));

            Assert.Equal(280, x);
            Assert.InRange(x, 100, 100 + 400 - 40);
        }

        [Fact]
        public void CenteredOverContentRightAligned_IsTheRightAlignedRestatement()
        {
            var room = WideOpen(190);
            Assert.Equal(
                JustifiedColumnTracks.CenteredOverContent(190, 60, 20, room),
                JustifiedColumnTracks.CenteredOverContentRightAligned(250, 60, 20, room));
        }

        // --- HeaderRoom: the bound is the gap to the NEIGHBOUR, not the
        // column's own band ---
        [Fact]
        public void RoomBounds_SplitTheGapBetweenTwoColumns_LeavingAWholeGutterBetweenTheHeaders()
        {
            // Left column's cells end at 300, right column's start at 340.
            const int leftInkRight = 300;
            const int rightInkLeft = 340;

            int leftColumnsRightBound =
                JustifiedColumnTracks.RoomRightBound(leftInkRight, rightInkLeft);
            int rightColumnsLeftBound =
                JustifiedColumnTracks.RoomLeftBound(leftInkRight, rightInkLeft);

            Assert.Equal(317, leftColumnsRightBound);
            Assert.Equal(323, rightColumnsLeftBound);
            Assert.Equal(
                JustifiedColumnTracks.HeaderGutter,
                rightColumnsLeftBound - leftColumnsRightBound);
        }

        [Fact]
        public void RoomBounds_NeverExcludeTheColumnsOwnInk()
        {
            // Columns 2px apart: splitting that gap would put the bound
            // inside the column it is meant to protect, so the bound stops
            // at the ink instead.
            Assert.Equal(302, JustifiedColumnTracks.RoomLeftBound(300, 302));
            Assert.Equal(300, JustifiedColumnTracks.RoomRightBound(300, 302));
        }

        [Fact]
        public void CenteredOverContent_HeaderTooWideForItsRoom_PinsLeftAndSpillsOneWay()
        {
            // 60px of word in 30px of room. It spills rightward only, the
            // one direction CenteredX already spills in, rather than
            // symmetrically into both neighbours.
            var room = JustifiedColumnTracks.HeaderRoom.Between(100, 130);

            Assert.Equal(100, JustifiedColumnTracks.CenteredOverContent(110, 10, 60, room));
        }

        [Fact]
        public void CenteredOverContent_NeighbourNearer_ClampsToTheNeighbourNotTheBand()
        {
            // The narrow-panel fallback: 14px between the values and the
            // column beside them. The header cannot fully centre, and gives
            // up only what the neighbour actually claims.
            var room = JustifiedColumnTracks.HeaderRoom.Between(
                0, JustifiedColumnTracks.RoomRightBound(500, 514));

            // Centring wants x=460; the bound pulls the header's right
            // edge back onto it, so it gives up 16px - not the 20 a clamp
            // into the 20px-wide value band would have taken.
            int x = JustifiedColumnTracks.CenteredOverContent(480, 20, 60, room);

            Assert.Equal(444, x);
            Assert.Equal(room.Right, x + 60);
        }

        [Fact]
        public void RightAlignedOverContent_EndsOnTheEdgeItsCellsEndOn()
        {
            // A money column right-aligns its values. Centring the word
            // over ink narrower than the word left it hanging past that
            // edge; this puts the two on one line.
            var room = JustifiedColumnTracks.HeaderRoom.Between(842, 1076);

            Assert.Equal(907, JustifiedColumnTracks.RightAlignedOverContent(975, 68, room));
            Assert.Equal(975, JustifiedColumnTracks.RightAlignedOverContent(975, 68, room) + 68);
        }

        [Fact]
        public void RightAlignedOverContent_IgnoresHowWideTheValuesAre()
        {
            // The edge is the whole rule. A column of "1c" and a column of
            // "1234g 56s 78c" seat their header identically.
            var room = JustifiedColumnTracks.HeaderRoom.Between(842, 1076);

            Assert.Equal(
                JustifiedColumnTracks.RightAlignedOverContent(975, 68, room),
                JustifiedColumnTracks.CenteredOverContentRightAligned(975, 68, 68, room));
        }

        [Fact]
        public void RightAlignedOverContent_NeighbourTooClose_PinsLeftAndSpillsOneWay()
        {
            // A header wider than the gap beside it cannot take the edge
            // without covering the column on its left, so it gives that up
            // and overhangs rightward - the one direction every other
            // header in this class already spills in.
            var room = JustifiedColumnTracks.HeaderRoom.Between(940, 1076);

            Assert.Equal(940, JustifiedColumnTracks.RightAlignedOverContent(975, 200, room));
        }

        [Fact]
        public void RightAlignedOverContent_RoomShorterThanTheEdge_KeepsTheHeaderInside()
        {
            // Nothing may cross the room's right bound, which for the last
            // column is the table's own pinned edge.
            var room = JustifiedColumnTracks.HeaderRoom.Between(842, 960);

            Assert.Equal(892, JustifiedColumnTracks.RightAlignedOverContent(975, 68, room));
        }

        [Fact]
        public void HeaderRoom_InvertedBounds_CollapseRatherThanInvert()
        {
            var room = JustifiedColumnTracks.HeaderRoom.Between(200, 100);

            Assert.Equal(200, room.Left);
            Assert.Equal(200, room.Right);
            Assert.Equal(0, room.Width);
            Assert.Equal(200, JustifiedColumnTracks.CenteredOverContent(150, 10, 40, room));
        }
    }
}
