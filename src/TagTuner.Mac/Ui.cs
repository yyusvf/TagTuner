namespace TagTuner.Mac;

internal static class Ui
{
    /// <summary>
    /// Views in einen Stapel legen. NSStackView hat in .NET keinen
    /// Konstruktor mit Views, und FromViews verträgt keinen Objektinitialisierer.
    ///
    /// </summary>
    public static T Arranged<T>(this T stack, params NSView[] views) where T : NSStackView
    {
        foreach (var v in views) stack.AddArrangedSubview(v);
        return stack;
    }

    /// <summary>
    /// Wie <see cref="Arranged"/>, aber jede View über die volle Breite, wie
    /// ein StackPanel unter Windows. NSStackView kennt das nicht von sich aus.
    /// </summary>
    public static T ArrangedFill<T>(this T stack, params NSView[] views) where T : NSStackView
    {
        stack.Alignment = NSLayoutAttribute.Leading;
        foreach (var v in views)
        {
            stack.AddArrangedSubview(v);
            stack.FillWidth(v);
        }
        return stack;
    }

    /// <summary>Die View bekommt die volle Breite des Stapels, ohne dessen Ränder.</summary>
    public static void FillWidth(this NSStackView stack, NSView v)
    {
        var insets = stack.EdgeInsets.Left + stack.EdgeInsets.Right;
        v.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -insets).Active = true;
    }
}
