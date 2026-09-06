using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PdfEditorApp.Plugins.Snake;

/// <summary>
/// High-performance Avalonia rendering surface for the Snake game.
/// Renders dark retro-arcade grid, animated snake segments, glowing food, and bonus stars via DrawingContext with zero memory allocations per frame.
/// </summary>
public class SnakeCanvasControl : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#0D1117"));
    private static readonly IPen GridLinePen = new Pen(new SolidColorBrush(Color.Parse("#161B22")), 0.75);
    private static readonly IBrush SnakeHeadBrush = new SolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush SnakeBodyBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush EyeWhiteBrush = Brushes.White;
    private static readonly IBrush EyePupilBrush = new SolidColorBrush(Color.Parse("#064E3B"));
    private static readonly IBrush FoodBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush FoodGlowBrush = new SolidColorBrush(Color.Parse("#40EF4444"));
    private static readonly IBrush BonusFoodBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush BonusFoodGlowBrush = new SolidColorBrush(Color.Parse("#50F59E0B"));

    private SnakeGameViewModel? _viewModel;

    public SnakeCanvasControl()
    {
        ClipToBounds = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null)
        {
            _viewModel.RenderRequested -= OnRenderRequested;
        }

        _viewModel = DataContext as SnakeGameViewModel;

        if (_viewModel != null)
        {
            _viewModel.RenderRequested += OnRenderRequested;
        }

        InvalidateVisual();
    }

    private void OnRenderRequested()
    {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // 1. Draw Dark Background
        context.FillRectangle(BackgroundBrush, bounds);

        double cellWidth = bounds.Width / SnakeGameViewModel.GridWidth;
        double cellHeight = bounds.Height / SnakeGameViewModel.GridHeight;

        // 2. Draw Subtle Grid Lines
        for (int x = 0; x <= SnakeGameViewModel.GridWidth; x++)
        {
            double lineX = x * cellWidth;
            context.DrawLine(GridLinePen, new Point(lineX, 0), new Point(lineX, bounds.Height));
        }

        for (int y = 0; y <= SnakeGameViewModel.GridHeight; y++)
        {
            double lineY = y * cellHeight;
            context.DrawLine(GridLinePen, new Point(0, lineY), new Point(bounds.Width, lineY));
        }

        if (_viewModel == null) return;

        // 3. Draw Bonus Food (Golden Orb)
        if (_viewModel.BonusFood.HasValue)
        {
            var bonusPt = _viewModel.BonusFood.Value;
            double bx = bonusPt.X * cellWidth + cellWidth / 2.0;
            double by = bonusPt.Y * cellHeight + cellHeight / 2.0;
            double bonusRadius = Math.Min(cellWidth, cellHeight) * 0.44;

            // Glow halo
            context.DrawEllipse(BonusFoodGlowBrush, null, new Point(bx, by), bonusRadius * 1.5, bonusRadius * 1.5);
            // Core
            context.DrawEllipse(BonusFoodBrush, null, new Point(bx, by), bonusRadius, bonusRadius);
        }

        // 4. Draw Regular Food (Red Glowing Orb)
        var food = _viewModel.Food;
        double fx = food.X * cellWidth + cellWidth / 2.0;
        double fy = food.Y * cellHeight + cellHeight / 2.0;
        double foodRadius = Math.Min(cellWidth, cellHeight) * 0.38;

        // Food glow
        context.DrawEllipse(FoodGlowBrush, null, new Point(fx, fy), foodRadius * 1.45, foodRadius * 1.45);
        // Food body
        context.DrawEllipse(FoodBrush, null, new Point(fx, fy), foodRadius, foodRadius);

        // 5. Draw Snake Segments
        bool isHead = true;
        foreach (var segment in _viewModel.SnakeBody)
        {
            double segX = segment.X * cellWidth + 1.0;
            double segY = segment.Y * cellHeight + 1.0;
            double segW = Math.Max(1.0, cellWidth - 2.0);
            double segH = Math.Max(1.0, cellHeight - 2.0);

            var segRect = new Rect(segX, segY, segW, segH);

            if (isHead)
            {
                // Head segment with rounded pill corners
                context.DrawRectangle(SnakeHeadBrush, null, new RoundedRect(segRect, 5.0));

                // Cute eyes
                double eyeSize = Math.Max(2.5, Math.Min(cellWidth, cellHeight) * 0.22);
                double pupilSize = eyeSize * 0.55;

                // Eye offsets
                double eyeOffset = cellWidth * 0.24;
                double eyeCenterX = segX + segW / 2.0;
                double eyeCenterY = segY + segH / 2.0;

                Point leftEye = new(eyeCenterX - eyeOffset, eyeCenterY - eyeOffset);
                Point rightEye = new(eyeCenterX + eyeOffset, eyeCenterY - eyeOffset);

                context.DrawEllipse(EyeWhiteBrush, null, leftEye, eyeSize, eyeSize);
                context.DrawEllipse(EyePupilBrush, null, leftEye, pupilSize, pupilSize);

                context.DrawEllipse(EyeWhiteBrush, null, rightEye, eyeSize, eyeSize);
                context.DrawEllipse(EyePupilBrush, null, rightEye, pupilSize, pupilSize);

                isHead = false;
            }
            else
            {
                // Body segment
                context.DrawRectangle(SnakeBodyBrush, null, new RoundedRect(segRect, 3.5));
            }
        }
    }
}
