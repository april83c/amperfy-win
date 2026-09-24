namespace Amperfy.Core.Tests;

public class SanityTest
{
    [Fact]
    public void NameIsAmperfy() => Assert.Equal("Amperfy", AmperfyInfo.Name);
}
