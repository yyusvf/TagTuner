namespace TagTuner.Mac;

internal static class Ui
{
    /// <summary>
    /// Views in einen Stapel legen. NSStackView hat in .NET keinen
    /// Konstruktor mit Views, und FromViews verträgt keinen Objektinitialisierer.
    /// </summary>
    public static T Arranged<T>(this T stack, params NSView[] views) where T : NSStackView
    {
        foreach (var v in views) stack.AddArrangedSubview(v);
        return stack;
    }
}
