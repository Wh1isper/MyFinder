namespace MyFinder.Core.Desktop;

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}
public sealed record Display(string Id, string Name, Rect Bounds, Rect Work, bool Primary);
public static class Presentation
{
    public static bool ShouldShow(bool intent, bool retreat, bool fullscreen, bool sessionLocked = false) => intent && !(retreat && fullscreen) && !sessionLocked;
    public static Display ChooseDisplay(IReadOnlyList<Display> displays, string preferred) =>
        displays.FirstOrDefault(x => x.Id == preferred) ?? displays.FirstOrDefault(x => x.Primary) ?? displays[0];
    public static Rect Place(Rect work, int width, int height, Placement placement)
    {
        width = Math.Clamp(width, 1, Math.Max(1, work.Width));
        height = Math.Clamp(height, 1, Math.Max(1, work.Height));
        int x = work.X + (int)Math.Round(Math.Clamp(placement.X, 0, 1) * (work.Width - width));
        int y = work.Y + (int)Math.Round(Math.Clamp(placement.Y, 0, 1) * (work.Height - height));
        if (placement.Alignment == Alignment.Top) y = work.Y;
        if (placement.Alignment == Alignment.Bottom) y = work.Bottom - height;
        if (placement.Alignment == Alignment.Left) x = work.X;
        if (placement.Alignment == Alignment.Right) x = work.Right - width;
        return new(x, y, width, height);
    }
    public static Placement Remember(Display display, Rect window, Alignment alignment = Alignment.Free) => new(display.Id,
        Math.Clamp((window.X - display.Work.X) / (double)Math.Max(1, display.Work.Width - window.Width), 0, 1),
        Math.Clamp((window.Y - display.Work.Y) / (double)Math.Max(1, display.Work.Height - window.Height), 0, 1), alignment);
    public static bool CoversDisplay(Rect window, Rect display, bool maximized) => !maximized &&
        window.X <= display.X + 2 && window.Y <= display.Y + 2 && window.Right >= display.Right - 2 && window.Bottom >= display.Bottom - 2;
}
