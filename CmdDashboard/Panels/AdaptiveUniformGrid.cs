using System;
using SW = System.Windows;
using SWControls = System.Windows.Controls.Primitives;

namespace CmdDashboard.Panels;

public class AdaptiveUniformGrid : SWControls.UniformGrid
{
    protected override SW.Size MeasureOverride(SW.Size constraint)
    {
        UpdateGrid();
        return base.MeasureOverride(constraint);
    }

    protected override void OnVisualChildrenChanged(SW.DependencyObject visualAdded, SW.DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        UpdateGrid();
    }

    private void UpdateGrid()
    {
        var count = Math.Max(InternalChildren.Count, 0);
        if (count == 0)
        {
            Rows = 0;
            Columns = 0;
            return;
        }

        var (rows, columns) = CalculateGrid(count);

        if (Rows != rows)
        {
            Rows = rows;
        }

        if (Columns != columns)
        {
            Columns = columns;
        }
    }

    private static (int rows, int columns) CalculateGrid(int count)
    {
        return count switch
        {
            1 => (1, 1),
            2 => (1, 2),
            3 => (1, 3),
            4 => (2, 2),
            _ => CalculateByRoot(count)
        };
    }

    private static (int rows, int columns) CalculateByRoot(int count)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling((double)count / columns);

        // Balance grid so rows and columns differ by at most 1 when possible
        while ((rows - 1) * columns >= count)
        {
            rows--;
        }

        while ((columns - 1) * rows >= count)
        {
            columns--;
        }

        return (rows, columns);
    }
}
