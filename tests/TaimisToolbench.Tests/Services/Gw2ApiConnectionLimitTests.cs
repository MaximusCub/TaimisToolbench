using System;
using System.Net;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class Gw2ApiConnectionLimitTests
    {
        [Fact]
        public void ApplyRaisesTheApiHostsConnectionLimitAndIsSafeToRepeat()
        {
            var servicePoint = ServicePointManager.FindServicePoint(new Uri("https://api.guildwars2.com"));

            // A ServicePoint the module never touched inherits this, and it
            // is what makes the raise below worth doing.
            Assert.True(ServicePointManager.DefaultConnectionLimit < Gw2ApiConnectionLimit.ConnectionsPerServer);

            Gw2ApiConnectionLimit.Apply();
            Assert.Equal(Gw2ApiConnectionLimit.ConnectionsPerServer, servicePoint.ConnectionLimit);

            Gw2ApiConnectionLimit.Apply();
            Assert.Equal(Gw2ApiConnectionLimit.ConnectionsPerServer, servicePoint.ConnectionLimit);
        }
    }
}
