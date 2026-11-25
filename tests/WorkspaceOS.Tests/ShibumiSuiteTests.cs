using System.Linq;
using WorkspaceOS.Core.Config;
using WorkspaceOS.Core.Shibumi;
using Xunit;

namespace WorkspaceOS.Tests
{
    /// <summary>
    /// The bar layout is driven by the Shibumi manifests, so these lock in the
    /// role set, the default sections and the user overrides that the Shibumi
    /// page writes.
    /// </summary>
    public class ShibumiSuiteTests
    {
        [Fact]
        public void CatalogueCarriesTheEighteenRoles()
        {
            var roles = ShibumiSuite.All
                .Select(p => p.Role)
                .Where(r => !string.IsNullOrWhiteSpace(r) && r.StartsWith("G"))
                .Distinct()
                .ToList();

            Assert.Equal(18, roles.Count);
            for (int i = 1; i <= 18; i++)
                Assert.Contains($"G{i}", roles);
        }

        [Fact]
        public void DefaultSectionsFollowTheManifests()
        {
            var cfg = new ShibumiConfig();
            var left = ShibumiSuite.KeysFor("left", cfg);
            var center = ShibumiSuite.KeysFor("center", cfg);
            var right = ShibumiSuite.KeysFor("right", cfg);

            Assert.Contains("Workspaces", left);
            Assert.Contains("CPU", left);
            Assert.Contains("RAM", left);
            Assert.Contains("Volume", left);
            Assert.Contains("Clock", center);
            Assert.Contains("Net", right);
            Assert.Contains("Battery", right);
            Assert.DoesNotContain("Net", left);
        }

        [Fact]
        public void SectionOverrideMovesAWidget()
        {
            var cfg = new ShibumiConfig();
            cfg.Sections["hancore.shibumi.cpu"] = "right";

            var left = ShibumiSuite.KeysFor("left", cfg);
            var right = ShibumiSuite.KeysFor("right", cfg);

            Assert.DoesNotContain("CPU", left);
            Assert.Contains("CPU", right);
        }

        [Fact]
        public void HiddenWidgetsLeaveTheBar()
        {
            var cfg = new ShibumiConfig();
            cfg.HiddenWidgets.Add("hancore.shibumi.network");

            Assert.DoesNotContain("Net", ShibumiSuite.KeysFor("right", cfg));
            Assert.True(ShibumiSuite.IsHidden(ShibumiSuite.Find("Net"),
                cfg));
        }

        [Fact]
        public void PluginsWindowsCannotServeAreMarkedUnavailable()
        {
            var ai = ShibumiSuite.Find("hancore.shibumi.ai");
            Assert.NotNull(ai);
            Assert.False(ai.Available);
            Assert.DoesNotContain("AI", ShibumiSuite.KeysFor("left", new ShibumiConfig()));
        }
    }
}
