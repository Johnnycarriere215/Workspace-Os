using WorkspaceOS.Core;
using Xunit;

namespace WorkspaceOS.Tests
{
    public class WindowsVersionTests
    {
        [Theory]
        [InlineData(17763)]   // Windows 10 1809
        [InlineData(19041)]   // Windows 10 2004
        [InlineData(19045)]   // Windows 10 22H2 — last Windows 10 build
        public void Windows10BuildsUseTheWin10DesktopSet(int build)
        {
            Assert.Equal("win10", WindowsVersion.DesktopInterfaceSet(build));
            Assert.Equal("10", WindowsVersion.FamilyName(build));
            Assert.True(WindowsVersion.SupportsDarkTitleBarOn(build));
        }

        [Fact]
        public void Pre2004Windows10UsesClassicBlur()
        {
            Assert.True(WindowsVersion.PrefersClassicBlurOn(17763));    // 1809
            Assert.True(WindowsVersion.PrefersClassicBlurOn(18363));    // 1909
            Assert.False(WindowsVersion.PrefersClassicBlurOn(19041));   // 2004
            Assert.False(WindowsVersion.PrefersClassicBlurOn(22621));   // Windows 11 22H2
        }

        [Fact]
        public void DarkTitleBarNeeds1809OrNewer()
        {
            Assert.False(WindowsVersion.SupportsDarkTitleBarOn(17134)); // 1803
            Assert.True(WindowsVersion.SupportsDarkTitleBarOn(17763));  // 1809
        }

        [Theory]
        [InlineData(22000, "win11")]        // 21H2
        [InlineData(22631, "win11")]        // 23H2
        [InlineData(26100, "win11-24h2")]   // 24H2
        [InlineData(26200, "win11-24h2")]   // 25H2
        public void Windows11BuildsUseTheManagerVtableThatMatchesThem(int build, string expected)
        {
            Assert.Equal(expected, WindowsVersion.DesktopInterfaceSet(build));
            Assert.False(WindowsVersion.PrefersClassicBlurOn(build));
        }

        [Fact]
        public void DescriptionNamesTheFamilyAndBuild()
        {
            Assert.Equal("Windows 10 (build 19045)", WindowsVersion.Describe(19045));
            Assert.Equal("Windows 11 24H2+ (build 26200)", WindowsVersion.Describe(26200));
        }
    }
}
