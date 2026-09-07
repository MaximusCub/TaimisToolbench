using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The four values /v2/characters/:id/equipment writes into a slot's
    /// "location" field, and the two questions the snapshot asks of them.
    /// </summary>
    public class EquipmentLocationPolicyTests
    {
        [Theory]
        [InlineData("Equipped")]
        [InlineData("Armory")]
        [InlineData("equipped")]
        [InlineData("ARMORY")]
        public void GearTheCharacterOwns_IsHeldByIt(string location)
        {
            Assert.True(EquipmentLocationPolicy.IsHeldByCharacter(location));
            Assert.False(EquipmentLocationPolicy.IsEquippedFromLegendaryArmory(location));
        }

        [Theory]
        [InlineData("EquippedFromLegendaryArmory")]
        [InlineData("LegendaryArmory")]
        public void ACopyDrawnFromTheArmory_IsNeverHeldByTheCharacter(string location)
        {
            // Counting one would multiply a single account-wide legendary by
            // the number of slots drawing it.
            Assert.False(EquipmentLocationPolicy.IsHeldByCharacter(location));
        }

        [Fact]
        public void OnlyTheWornArmoryCopyNamesItsCharacter()
        {
            Assert.True(
                EquipmentLocationPolicy.IsEquippedFromLegendaryArmory("EquippedFromLegendaryArmory"));

            // A saved equipment template the character is not wearing. The
            // reader asked who has the item equipped.
            Assert.False(EquipmentLocationPolicy.IsEquippedFromLegendaryArmory("LegendaryArmory"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("SomeFutureLocation")]
        public void AValueTheEndpointHasNotShippedYet_IsNeitherHeldNorWorn(string location)
        {
            Assert.False(EquipmentLocationPolicy.IsHeldByCharacter(location));
            Assert.False(EquipmentLocationPolicy.IsEquippedFromLegendaryArmory(location));
        }
    }
}
