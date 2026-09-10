using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class ApiPermissionGapTests
    {
        // What the module's manifest declares.
        private static readonly string[] Declared =
        {
            "account", "characters", "inventories", "wallet", "unlocks", "builds", "progression",
        };

        /// <summary>
        /// The owner's stored list, which is what every user who enabled
        /// the module before builds and progression were declared still
        /// carries.
        /// </summary>
        [Fact]
        public void A_permission_added_after_the_user_first_enabled_the_module_reads_as_unapproved()
        {
            var approved = new[] { "account", "characters", "inventories", "wallet", "unlocks" };

            var unapproved = ApiPermissionGap.Unapproved(Declared, approved);

            Assert.Equal(new[] { "builds", "progression" }, unapproved);
        }

        [Fact]
        public void An_account_that_approved_everything_declared_has_no_gap()
        {
            Assert.Empty(ApiPermissionGap.Unapproved(Declared, Declared));
        }

        [Fact]
        public void Approvals_beyond_what_the_module_declares_are_not_a_gap()
        {
            var approved = new[] { "account", "characters", "inventories", "wallet", "unlocks", "builds", "progression", "pvp" };

            Assert.Empty(ApiPermissionGap.Unapproved(Declared, approved));
        }

        [Fact]
        public void Nothing_approved_yet_means_every_declared_permission_is_missing()
        {
            Assert.Equal(Declared, ApiPermissionGap.Unapproved(Declared, new string[0]));
            Assert.Equal(Declared, ApiPermissionGap.Unapproved(Declared, null));
        }

        [Fact]
        public void Names_are_compared_without_regard_to_case()
        {
            var approved = new[] { "Account", "CHARACTERS", "Inventories", "Wallet", "Unlocks", "Builds", "Progression" };

            Assert.Empty(ApiPermissionGap.Unapproved(Declared, approved));
        }

        [Fact]
        public void A_declaration_read_from_nothing_yields_nothing()
        {
            Assert.Empty(ApiPermissionGap.Unapproved(null, Declared));
        }

        [Fact]
        public void The_message_names_the_permissions_and_what_to_do()
        {
            var message = ApiPermissionGap.Compose(new[] { "builds", "progression" });

            Assert.Contains("2 API permissions", message);
            Assert.Contains("builds, progression", message);
            Assert.Contains("Manage Modules", message);
        }

        [Fact]
        public void One_missing_permission_reads_as_one()
        {
            var message = ApiPermissionGap.Compose(new[] { "progression" });

            Assert.Contains("1 API permission this module asks for is not approved: progression.", message);
            Assert.Equal("1 API permission not approved - see the Log tab",
                ApiPermissionGap.ComposeStatus(new[] { "progression" }));
        }

        [Fact]
        public void Nothing_missing_composes_nothing()
        {
            Assert.Null(ApiPermissionGap.Compose(new string[0]));
            Assert.Null(ApiPermissionGap.ComposeStatus(new string[0]));
            Assert.Null(ApiPermissionGap.Compose(null));
            Assert.Null(ApiPermissionGap.ComposeStatus(null));
        }
    }
}
