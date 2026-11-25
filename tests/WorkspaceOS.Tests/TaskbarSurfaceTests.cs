using WorkspaceOS.Core.Taskbar;
using Xunit;

namespace WorkspaceOS.Tests
{
    public class TaskbarSurfaceTests
    {
        [Theory]
        [InlineData("Acrylic", TaskbarBackdropMode.Acrylic)]
        [InlineData("clear", TaskbarBackdropMode.Clear)]
        [InlineData("blur", TaskbarBackdropMode.Blur)]
        [InlineData("disabled", TaskbarBackdropMode.Disabled)]
        [InlineData("", TaskbarBackdropMode.Dynamic)]
        [InlineData("something-else", TaskbarBackdropMode.Dynamic)]
        public void Parse_MapsConfiguredNames(string raw, TaskbarBackdropMode expected)
        {
            Assert.Equal(expected, TaskbarSurface.Parse(raw));
        }

        [Fact]
        public void Dynamic_IsClearOnTheDesktop()
        {
            Assert.Equal(TaskbarBackdropMode.Clear,
                TaskbarSurface.Resolve(TaskbarBackdropMode.Dynamic, desktopForeground: true, maximized: false));
        }

        [Fact]
        public void Dynamic_IsBlurOverANormalWindow()
        {
            Assert.Equal(TaskbarBackdropMode.Blur,
                TaskbarSurface.Resolve(TaskbarBackdropMode.Dynamic, desktopForeground: false, maximized: false));
        }

        [Fact]
        public void Dynamic_IsAcrylicOverAMaximizedWindow()
        {
            Assert.Equal(TaskbarBackdropMode.Acrylic,
                TaskbarSurface.Resolve(TaskbarBackdropMode.Dynamic, desktopForeground: false, maximized: true));
        }

        [Fact]
        public void ExplicitMode_IgnoresTheForeground()
        {
            Assert.Equal(TaskbarBackdropMode.Clear,
                TaskbarSurface.Resolve(TaskbarBackdropMode.Clear, desktopForeground: false, maximized: true));
            Assert.Equal(TaskbarBackdropMode.Blur,
                TaskbarSurface.Resolve(TaskbarBackdropMode.Blur, desktopForeground: true, maximized: false));
        }
    }
}
