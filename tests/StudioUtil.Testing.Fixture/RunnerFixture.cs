using System;
using System.Threading.Tasks;
using TUnit.Core;

namespace StudioUtil.Testing.Fixture;

public sealed class RunnerFixture
{
    [Test]
    public void Passing() => Console.WriteLine("fixture stdout");

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public void DataDriven(int value) => Console.WriteLine($"case {value}");

    [Test]
    public void Failing()
    {
        Console.Error.WriteLine("fixture stderr");
        throw new InvalidOperationException("fixture failure");
    }

    [Test]
    [Skip("fixture skipped")]
    public void Skipped() { }

    [Test]
    public async Task Slow() => await Task.Delay(TimeSpan.FromMinutes(2));
}
