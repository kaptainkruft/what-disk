using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatDisk.Hosting;
using WhatDisk.Views;

namespace WhatDisk.Tests.Platform;

/// <summary>
/// Tests reading the version from assembly attributes and showing it in the About window.
/// </summary>
public sealed class AppVersionInfoTests
{
    /// <summary>
    /// A release build's informational version and timestamp are split into version, commit and release time.
    /// </summary>
    [Fact]
    public void Parse_ReadsAReleaseBuild()
    {
        var info = AppVersionInfo.Parse("WhatDisk", "0.1.3-alpha+907dc164e175b3eaeff2cacd1b90221756d946b9", "2026-10-17T04:12:09Z", "Copyright (c) 2026");

        Assert.Equal("0.1.3-alpha", info.Version);
        Assert.Equal("907dc16", info.Commit);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 4, 12, 9, TimeSpan.Zero), info.BuildTimestamp);
        Assert.Equal("Version 0.1.3-alpha", info.VersionText);
        Assert.Equal("Released 2026-10-17 04:12 UTC", info.ReleasedText);
        Assert.Equal("Commit 907dc16", info.CommitText);
    }

    /// <summary>
    /// Builds without a timestamp, commit or version are described as development builds.
    /// </summary>
    [Fact]
    public void Parse_DescribesDevelopmentBuilds()
    {
        var info = AppVersionInfo.Parse(null, null, null, null);

        Assert.Equal("WhatDisk", info.ProductName);
        Assert.Equal("0.0.0-dev", info.Version);
        Assert.Null(info.Commit);
        Assert.Equal("Development build", info.ReleasedText);
        Assert.Equal(string.Empty, info.CommitText);
        Assert.Equal("Development build", AppVersionInfo.Parse("WhatDisk", "1.0.0", "not a date", null).ReleasedText);
    }

    /// <summary>
    /// The shared assembly carries the product name and a development version in local builds.
    /// </summary>
    [Fact]
    public void FromAssembly_ReadsTheAssemblyAttributes()
    {
        var info = AppVersionInfo.FromAssembly(typeof(AppVersionInfo).Assembly);

        Assert.Equal("WhatDisk", info.ProductName);
        Assert.Matches(@"^\d+\.\d+\.\d+", info.Version);
        Assert.Contains("2026", info.Copyright, StringComparison.Ordinal);
    }

    /// <summary>
    /// The About box shows the name, version and release time.
    /// </summary>
    [AvaloniaFact]
    public void AboutView_ShowsTheVersion()
    {
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = new AboutView(AppVersionInfo.Parse("WhatDisk", "0.1.3-alpha+907dc164e1", "2026-10-17T04:12:09Z", "Copyright (c) 2026 kaptainkruft")) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(static text => text.Text).ToArray();
        Assert.Contains("Version 0.1.3-alpha", texts);
        Assert.Contains("Released 2026-10-17 04:12 UTC", texts);
        Assert.Contains("Commit 907dc16", texts);
        Assert.Contains("WhatDisk", texts);
        if (Environment.GetEnvironmentVariable("WHATDISK_SCREENSHOTS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            window.CaptureRenderedFrame()!.Save(Path.Join(directory, "about.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        window.Close();
    }
}
