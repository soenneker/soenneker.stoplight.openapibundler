using Soenneker.Stoplight.OpenApiBundler.Abstract;
using Soenneker.Tests.HostedUnit;

namespace Soenneker.Stoplight.OpenApiBundler.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class StoplightOpenApiBundlerTests : HostedUnitTest
{
    private readonly IStoplightOpenApiBundler _util;

    public StoplightOpenApiBundlerTests(Host host) : base(host)
    {
        _util = Resolve<IStoplightOpenApiBundler>(true);
    }

    [Test]
    public void Default()
    {

    }
}
