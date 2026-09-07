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

        [Fact]
        public void IconApplyRaisesTheAssetHostsConnectionLimitAndIsSafeToRepeat()
        {
            var servicePoint = ServicePointManager.FindServicePoint(new Uri("https://assets.gw2dat.com"));

            Assert.True(ServicePointManager.DefaultConnectionLimit < IconAssetConnectionLimit.ConnectionsPerServer);

            IconAssetConnectionLimit.Apply();
            Assert.Equal(IconAssetConnectionLimit.ConnectionsPerServer, servicePoint.ConnectionLimit);

            IconAssetConnectionLimit.Apply();
            Assert.Equal(IconAssetConnectionLimit.ConnectionsPerServer, servicePoint.ConnectionLimit);
        }

        [Fact]
        public void ApplyLeavesEveryOtherHostAndTheProcessDefaultAlone()
        {
            // The two raises must stay host-scoped so Blish HUD's other
            // traffic keeps whatever it had. render.guildwars2.com stands in
            // for that traffic here, and is also the host the icon path was
            // believed to use: Blish HUD 1.3.0's
            // ContentService.GetRenderServiceTexture drops the render
            // signature and fetches from assets.gw2dat.com instead, so a
            // raise on this one would be a raise nothing reads.
            var other = ServicePointManager.FindServicePoint(new Uri("https://render.guildwars2.com"));

            Gw2ApiConnectionLimit.Apply();
            IconAssetConnectionLimit.Apply();

            Assert.Equal(2, ServicePointManager.DefaultConnectionLimit);
            Assert.Equal(ServicePointManager.DefaultConnectionLimit, other.ConnectionLimit);
        }
    }
}
