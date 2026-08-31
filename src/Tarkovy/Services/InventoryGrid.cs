namespace Tarkovy.Services;

/// <summary>Aligns clicks to Tarkov inventory slot boundaries.</summary>
internal static class InventoryGrid
{
    public static readonly int[] PhaseOffsets = [0, -9, 9, -18, 18];

    /// <summary>Screen rects for slot sizes that contain the click, with grid phase search.</summary>
    public static IEnumerable<(int Left, int Top, int Width, int Height)> SnappedSlotsAt(
        int clickX, int clickY, int slotW, int slotH)
    {
        var slot = ScreenCapture.ItemSlotPx();
        var regionSize = slot * 7;
        var originX = clickX - regionSize / 2;
        var originY = clickY - regionSize / 2;
        var localX = clickX - originX;
        var localY = clickY - originY;
        var seen = new HashSet<string>();

        foreach (var phaseX in PhaseOffsets)
        foreach (var phaseY in PhaseOffsets)
        {
            if (!TryGetSlotRect(localX, localY, phaseX, phaseY, slotW, slotH, slot, out var sx, out var sy))
                continue;

            var key = $"{sx}:{sy}:{slotW}x{slotH}";
            if (!seen.Add(key)) continue;

            var (pw, ph) = ScreenCapture.SlotPixelSize(slotW, slotH);
            yield return (originX + sx, originY + sy, pw, ph);
        }
    }

    private static bool TryGetSlotRect(
        int localX, int localY, int phaseX, int phaseY,
        int slotW, int slotH, int slot, out int sx, out int sy)
    {
        sx = sy = 0;
        var adjX = localX - phaseX;
        var adjY = localY - phaseY;
        var col = (int)Math.Floor((adjX + slot * 0.5) / (double)slot);
        var row = (int)Math.Floor((adjY + slot * 0.5) / (double)slot);

        for (var dw = 0; dw < slotW; dw++)
        for (var dh = 0; dh < slotH; dh++)
        {
            var x = (col - dw) * slot;
            var y = (row - dh) * slot;
            if (localX >= x && localX < x + slotW * slot && localY >= y && localY < y + slotH * slot)
            {
                sx = x;
                sy = y;
                return true;
            }
        }

        return false;
    }
}
