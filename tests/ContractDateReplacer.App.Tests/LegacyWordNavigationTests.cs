using ContractDateReplacer.App.Services;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Services;
using ContractDateReplacer.Infrastructure.Services;
using ContractDateReplacer.Word;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ContractDateReplacer.App.Tests;

public class LegacyWordNavigationTests
{
    [Fact]
    [Trait("Category", "WordIntegration")]
    public async Task LegacyDoc_ScanReplaceVerify_PreservesOriginal()
    {
        var wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType == null) return; // Optional integration check on machines with Microsoft Word.
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LegacyWordSmoke_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        string source = System.IO.Path.Combine(dir, "Contract.doc");
        string output = System.IO.Path.Combine(dir, "Contract_modified.docx");
        dynamic? app = null;
        dynamic? doc = null;
        try
        {
            app = Activator.CreateInstance(wordType);
            app!.Visible = false;
            app.DisplayAlerts = 0;
            doc = app.Documents.Add();
            doc.Content.Text = "Ngay ky hop dong: 02/09/2026";
            doc.SaveAs2(source, 0);
            doc.Close(false);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(doc);
            doc = null;
            app.Quit(false);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
            app = null;
            var original = System.IO.File.ReadAllBytes(source);
            var scanner = new WordDocumentScanner();
            var validator = new WordOpenXmlValidator();
            var router = new DocumentFormatRouter(scanner,
                new WordDocumentReplacementService(validator, NullLogger<WordDocumentReplacementService>.Instance),
                new WordDocumentVerificationService(scanner, new DateDetector(), validator, NullLogger<WordDocumentVerificationService>.Instance),
                new ExcelDocumentService());
            var segments = await router.ScanDocumentAsync(source);
            var segment = Assert.Single(segments.Where(s => s.LogicalText.Contains("02/09/2026")));
            var occurrence = new ContractDateReplacer.Core.Models.ReplacementOccurrence
            {
                FilePath = source, Segment = segment, IsSelected = true,
                Candidate = new ContractDateReplacer.Core.Models.DateCandidate
                {
                    RawText = "02/09/2026", Length = 10,
                    StartIndex = segment.LogicalText.IndexOf("02/09/2026"), ParsedDate = new DateTime(2026, 9, 2)
                },
                NewDateText = "26/10/2026", NewDateTime = new DateTime(2026, 10, 26)
            };
            Assert.True(await router.ReplaceDatesAsync(source, output, new[] { occurrence }));
            var verified = await router.VerifyOutputAsync(source, output, new[] { occurrence });
            Assert.True(verified.IsPassed, string.Join("; ", verified.Issues));
            Assert.Equal(original, System.IO.File.ReadAllBytes(source));
        }
        finally
        {
            if (doc != null) { doc.Close(false); System.Runtime.InteropServices.Marshal.ReleaseComObject(doc); }
            if (app != null) { app.Quit(false); System.Runtime.InteropServices.Marshal.ReleaseComObject(app); }
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(".doc")]
    [InlineData(".DOC")]
    public async Task Router_AcceptsLegacyWord(string extension)
    {
        var scanner = new WordDocumentScanner();
        var validator = new WordOpenXmlValidator();
        var router = new DocumentFormatRouter(scanner,
            new WordDocumentReplacementService(validator, NullLogger<WordDocumentReplacementService>.Instance),
            new WordDocumentVerificationService(scanner, new DateDetector(), validator, NullLogger<WordDocumentVerificationService>.Instance),
            new ExcelDocumentService());
        Assert.False(await router.CanReadAsync(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + extension)));
    }

    [Fact]
    public void Execution_CannotNavigateAwayWhileRunning()
    {
        var vm = new ExecutionViewModel(Mock.Of<IDocumentReplacementService>(), Mock.Of<IDocumentVerificationService>(),
            Mock.Of<IBackupRollbackService>(), Mock.Of<IAppDataService>(), NullLogger<ExecutionViewModel>.Instance);
        int navigations = 0;
        vm.RequestNavigateBack += () => navigations++;
        vm.IsRunning = true;
        vm.NavigateBack();
        Assert.Equal(0, navigations);
        vm.IsRunning = false;
        vm.NavigateBack();
        Assert.Equal(1, navigations);
    }

    [Fact]
    public void Report_CanNavigateBackWithoutResettingBatch()
    {
        var vm = new ReportViewModel(Mock.Of<IAppDataService>(), NullLogger<ReportViewModel>.Instance);
        var eventInfo = typeof(ReportViewModel).GetEvent("RequestNavigateBack");
        Assert.NotNull(eventInfo);
        int navigations = 0;
        eventInfo!.AddEventHandler(vm, (Action)(() => navigations++));
        var method = typeof(ReportViewModel).GetMethod("NavigateBack");
        Assert.NotNull(method);
        method!.Invoke(vm, null);
        Assert.Equal(1, navigations);
    }
}
