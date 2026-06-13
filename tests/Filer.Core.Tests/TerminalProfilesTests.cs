using Filer.Core;

namespace Filer.Core.Tests;

public sealed class TerminalProfilesTests
{
    [Fact]
    public void Detect_AllShellsExist_ReturnsAllInOrder()
    {
        var profiles = TerminalProfiles.Detect(_ => true);

        Assert.Equal(4, profiles.Count);
        Assert.Equal("PowerShell", profiles[0].Name);
        Assert.Equal("PowerShell 7", profiles[1].Name);
        Assert.Equal("コマンド プロンプト", profiles[2].Name);
        Assert.Equal("Git Bash", profiles[3].Name);
    }

    [Fact]
    public void Detect_OnlySystemShells_ReturnsPowerShellAndCmd()
    {
        // pwsh / Git Bash が未インストール(= System32 配下のみ存在)の環境。
        var profiles = TerminalProfiles.Detect(p =>
            p.Contains(@"\System32\", StringComparison.OrdinalIgnoreCase) ||
            p.Contains(@"\WindowsPowerShell\", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, profiles.Count);
        Assert.Equal("PowerShell", profiles[0].Name);
        Assert.Equal("コマンド プロンプト", profiles[1].Name);
    }

    [Fact]
    public void Detect_ExePaths_PointToExpectedExecutables()
    {
        var profiles = TerminalProfiles.Detect(_ => true);

        Assert.EndsWith("powershell.exe", profiles[0].ExePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("pwsh.exe", profiles[1].ExePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("cmd.exe", profiles[2].ExePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("bash.exe", profiles[3].ExePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_GitBash_HasLoginInteractiveArgs()
    {
        var profiles = TerminalProfiles.Detect(_ => true);

        Assert.Equal("--login -i", profiles[3].Arguments);
        Assert.Equal(string.Empty, profiles[0].Arguments);
    }

    [Fact]
    public void Detect_RealEnvironment_AlwaysContainsAtLeastPowerShellAndCmd()
    {
        // 実環境(File.Exists)でも PowerShell と cmd は必ず存在する。
        var profiles = TerminalProfiles.Detect();

        Assert.Contains(profiles, p => p.Name == "PowerShell");
        Assert.Contains(profiles, p => p.Name == "コマンド プロンプト");
    }
}
