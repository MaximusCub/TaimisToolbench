using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class PopoutOpacityTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(49)]
        public void Clamp_CannotBeTakenBelowTheFloor(int percent)
        {
            Assert.Equal(PopoutOpacity.MinPercent, PopoutOpacity.Clamp(percent));
        }

        [Theory]
        [InlineData(101)]
        [InlineData(int.MaxValue)]
        public void Clamp_CannotBeTakenAboveSolid(int percent)
        {
            Assert.Equal(PopoutOpacity.MaxPercent, PopoutOpacity.Clamp(percent));
        }

        [Fact]
        public void Clamp_LeavesAValueInsideTheBandAlone()
        {
            for (int percent = PopoutOpacity.MinPercent; percent <= PopoutOpacity.MaxPercent; percent++)
            {
                Assert.Equal(percent, PopoutOpacity.Clamp(percent));
            }
        }

        /// <summary>
        /// The window is faded by writing this factor into Control.Opacity,
        /// so the floor has to survive the conversion as well as the clamp:
        /// a factor of zero is an invisible window whatever the slider says.
        /// </summary>
        [Fact]
        public void ToFactor_NeverReachesInvisible()
        {
            Assert.Equal(0.5f, PopoutOpacity.ToFactor(0));
            Assert.Equal(0.5f, PopoutOpacity.ToFactor(PopoutOpacity.MinPercent));
            Assert.Equal(1f, PopoutOpacity.ToFactor(PopoutOpacity.MaxPercent));
        }

        [Fact]
        public void ToFactor_StaysAboveTheModulesOwnStalePlanWash()
        {
            // 0.45f is what the Crafting Plan tab fades a stale plan to, and
            // that wash exists to read as "not current". A popout is being
            // read, so its floor sits over it.
            Assert.True(PopoutOpacity.ToFactor(PopoutOpacity.MinPercent) > 0.45f);
        }

        /// <summary>
        /// The reported defect: a stored opacity was gone the next time the
        /// window was opened. A Blish window sets its own Opacity to 0 on
        /// every Show and animates it to 1, so the stored value has to be
        /// re-imposed per frame; capping is what stops the animation
        /// passing it.
        /// </summary>
        [Fact]
        public void CapToStored_HoldsAShowAnimationAtTheStoredValue()
        {
            const int Stored = 70;
            float highest = 0f;

            for (int frame = 0; frame <= 10; frame++)
            {
                float held = PopoutOpacity.CapToStored(frame / 10f, Stored);
                if (held > highest)
                {
                    highest = held;
                }
            }

            Assert.Equal(PopoutOpacity.ToFactor(Stored), highest);
        }

        /// <summary>
        /// The other half: the hide animation drives Opacity DOWN to 0, and
        /// a window whose Opacity never reaches 0 is never made invisible.
        /// Capping leaves that descent alone.
        /// </summary>
        [Fact]
        public void CapToStored_LeavesAHideAnimationToReachZero()
        {
            const int Stored = 70;
            float last = 1f;

            for (int frame = 10; frame >= 0; frame--)
            {
                last = PopoutOpacity.CapToStored(frame / 10f, Stored);
            }

            Assert.Equal(0f, last);
        }

        [Fact]
        public void CapToStored_NeverRaisesAValueAlreadyUnderTheStoredOne()
        {
            Assert.Equal(0.2f, PopoutOpacity.CapToStored(0.2f, 100));
            Assert.Equal(0.5f, PopoutOpacity.CapToStored(0.5f, 70));
        }

        [Fact]
        public void CapToStored_ClampsAStoredValueOutsideTheBand()
        {
            Assert.Equal(
                PopoutOpacity.ToFactor(PopoutOpacity.MinPercent),
                PopoutOpacity.CapToStored(1f, 0));
            Assert.Equal(
                PopoutOpacity.ToFactor(PopoutOpacity.MaxPercent),
                PopoutOpacity.CapToStored(1f, 400));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void CapToStored_ReplacesAnUnreadableValueWithTheStoredOne(float current)
        {
            Assert.Equal(PopoutOpacity.ToFactor(80), PopoutOpacity.CapToStored(current, 80));
        }

        [Theory]
        [InlineData(72.4f, 72)]
        [InlineData(72.6f, 73)]
        [InlineData(-40f, PopoutOpacity.MinPercent)]
        [InlineData(400f, PopoutOpacity.MaxPercent)]
        public void TryPercentFromSliderValue_RoundsAndClamps(float value, int expected)
        {
            int percent;
            Assert.True(PopoutOpacity.TryPercentFromSliderValue(value, out percent));
            Assert.Equal(expected, percent);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void TryPercentFromSliderValue_RefusesANonNumber(float value)
        {
            int percent;
            Assert.False(PopoutOpacity.TryPercentFromSliderValue(value, out percent));
        }
    }
}
