using Haitch.Roslyn.Types;

namespace Haitch.Roslyn.PackageSmoke;

internal static class Usage
{
    internal static bool ArraysAreEqual(int[] left, int[] right)
    {
        EquatableArray<int> leftArray = new(left);
        EquatableArray<int> rightArray = new(right);

        return leftArray.Equals(rightArray);
    }
}
