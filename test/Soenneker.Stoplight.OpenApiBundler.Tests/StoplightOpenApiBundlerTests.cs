using Soenneker.Stoplight.OpenApiBundler.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Stoplight.OpenApiBundler.Tests;

[Collection("Collection")]
public sealed class StoplightOpenApiBundlerTests : FixturedUnitTest
{
    private readonly IStoplightOpenApiBundler _util;

    public StoplightOpenApiBundlerTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IStoplightOpenApiBundler>(true);
    }

    [Fact]
    public void Default()
    {

    }
}
