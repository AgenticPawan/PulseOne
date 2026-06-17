using PulseOne.SharedKernel.MultiTenancy;
using Xunit;

namespace PulseOne.SharedKernel.Tests;

/// <summary>
/// The fail-closed tenant context is PulseOne's most critical security primitive
/// (CLAUDE.md security rule #2). These tests assert it NEVER yields a default tenant.
/// </summary>
public sealed class TenantContextTests
{
    [Fact]
    public void TenantId_WhenUnresolved_Throws()
    {
        var sut = new TenantContext();

        Assert.False(sut.IsResolved);
        Assert.Throws<TenantResolutionException>(() => _ = sut.TenantId);
    }

    [Fact]
    public void Resolve_ThenTenantId_ReturnsResolvedValue()
    {
        var sut = new TenantContext();

        sut.Resolve("contoso");

        Assert.True(sut.IsResolved);
        Assert.Equal("contoso", sut.TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resolve_WithEmptyOrWhitespace_Throws(string? tenantId)
    {
        var sut = new TenantContext();

        Assert.Throws<TenantResolutionException>(() => sut.Resolve(tenantId!));
        Assert.False(sut.IsResolved);
    }

    [Fact]
    public void TenantId_NeverReturnsDefaultLiteral()
    {
        var sut = new TenantContext();

        // Guard against the exact v1 defect: a silent "default" tenant.
        var ex = Record.Exception(() => _ = sut.TenantId);
        Assert.IsType<TenantResolutionException>(ex);
    }
}
