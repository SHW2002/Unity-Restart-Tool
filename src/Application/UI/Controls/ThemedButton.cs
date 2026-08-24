namespace UnityRestartTool.UI.Controls;

internal sealed class ThemedButton : Button
{
    private bool _hovered;

    public ThemedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        Invalidate();
        base.OnEnabledChanged(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Color background = !Enabled
            ? Color.FromArgb(31, 35, 43)
            : _hovered ? FlatAppearance.MouseOverBackColor
            : BackColor;
        Color border = Enabled
            ? FlatAppearance.BorderColor
            : Color.FromArgb(52, 58, 70);
        Color text = Enabled
            ? ForeColor
            : Color.FromArgb(116, 124, 140);

        using SolidBrush backgroundBrush = new(background);
        using Pen borderPen = new(border);
        eventArgs.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        eventArgs.Graphics.DrawRectangle(
            borderPen,
            0,
            0,
            Math.Max(0, Width - 1),
            Math.Max(0, Height - 1));
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            ClientRectangle,
            text,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }
}
