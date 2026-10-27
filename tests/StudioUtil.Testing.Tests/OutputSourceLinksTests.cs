using System;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class OutputSourceLinksTests
{
    [Test]
    public async Task FindsExceptionFramesAndCompilerLocationsWithoutChangingTheirText()
    {
        const string output = "System.Exception: failed\r\n   at Example.Run() in C:\\source folder\\Test (copy).cs:line 42\r\n" +
            "   at Inner.Run() in \\\\server\\repo\\Other.cs:line 9\n" +
            "C:\\source folder\\Build.cs(18,7): error CS1002: expected ;\n" +
            "   at Unix.Run() in /repo/Tests.cs:line 12\n" +
            "   at Relative.Run() in ./Tests.cs:line 3";
        var links = OutputSourceLinks.Parse(output);
        await Assert.That(links.Count).IsEqualTo(5);
        await Assert.That(links[0].Path).IsEqualTo(@"C:\source folder\Test (copy).cs");
        await Assert.That(links[0].Line).IsEqualTo(42);
        await Assert.That(output.Substring(links[0].Start, links[0].Length)).IsEqualTo(@"C:\source folder\Test (copy).cs:line 42");
        await Assert.That(links[2].Column).IsEqualTo(7);
        await Assert.That(links[3].Path).IsEqualTo("/repo/Tests.cs");
        await Assert.That(links[4].Path).IsEqualTo("./Tests.cs");
    }

    [Test]
    public async Task IgnoresNonSourceTextAndInvalidLineNumbers()
    {
        var links = OutputSourceLinks.Parse("https://example.com/code:line 5\nfile:///C:/test.cs:line 9\n" +
            "at Test() in C:\\Test.cs:line 0\nat Test() in C:\\Test.cs:line 9999999999999\n" +
            "C:\\Test.cs(1,0): invalid column\nplain output\n");
        await Assert.That(links.Count).IsEqualTo(0);
    }
}
