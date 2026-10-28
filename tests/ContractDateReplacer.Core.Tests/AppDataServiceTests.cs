using System;
using System.IO;
using ContractDateReplacer.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class AppDataServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly AppDataService _appDataService;

    public AppDataServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ContractDateReplacer_Tests_" + Guid.NewGuid().ToString("N"));
        _appDataService = new AppDataService(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, true); } catch { }
        }
    }

    [Fact]
    public void AppDataService_ShouldCreateStandardDirectories()
    {
        Directory.Exists(_appDataService.RootDirectory).Should().BeTrue();
        Directory.Exists(_appDataService.LogsDirectory).Should().BeTrue();
        Directory.Exists(_appDataService.TempDirectory).Should().BeTrue();
        _appDataService.DatabasePath.Should().EndWith("ContractDateReplacer.db");
    }

    [Fact]
    public void GetUniqueOutputFilePath_WhenFileDoesNotExist_ReturnsOriginalPath()
    {
        string targetDir = _appDataService.TempDirectory;
        string fileName = "HopDong_Test.docx";

        string uniquePath = _appDataService.GetUniqueOutputFilePath(targetDir, fileName);

        uniquePath.Should().Be(Path.Combine(targetDir, fileName));
    }

    [Fact]
    public void GetUniqueOutputFilePath_WhenFileExists_ReturnsNumberedPath()
    {
        string targetDir = _appDataService.TempDirectory;
        string fileName = "HopDong_Test.docx";
        string existingFile = Path.Combine(targetDir, fileName);
        File.WriteAllText(existingFile, "dummy content");

        string uniquePath = _appDataService.GetUniqueOutputFilePath(targetDir, fileName);

        uniquePath.Should().Be(Path.Combine(targetDir, "HopDong_Test_1.docx"));
    }
}
