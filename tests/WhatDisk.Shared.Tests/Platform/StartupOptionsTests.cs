using WhatDisk.Hosting;

namespace WhatDisk.Tests.Platform;

/// <summary>
/// Tests command-line parsing.
/// </summary>
public sealed class StartupOptionsTests
{
    /// <summary>
    /// No arguments means no startup scan.
    /// </summary>
    [Fact]
    public void Parse_WithoutArgumentsReturnsNone()
    {
        Assert.Same(StartupOptions.None, StartupOptions.Parse(null));
        Assert.Same(StartupOptions.None, StartupOptions.Parse([]));
    }

    /// <summary>
    /// The first non-option argument is the path; the exit option is recognised; unknown options are ignored.
    /// </summary>
    [Fact]
    public void Parse_ReadsThePathAndExitOption()
    {
        Assert.Equal(new StartupOptions("/Users", false), StartupOptions.Parse(["-NSDocumentRevisionsDebugMode", "/Users"]));
        Assert.Equal(new StartupOptions("/Users", true), StartupOptions.Parse(["/Users", "--exit-when-done"]));
    }

    /// <summary>
    /// Asking to exit without a path to scan is ignored.
    /// </summary>
    [Fact]
    public void Parse_IgnoresTheExitOptionWithoutAPath() =>
        Assert.Equal(new StartupOptions(null, false), StartupOptions.Parse(["--exit-when-done"]));
}
