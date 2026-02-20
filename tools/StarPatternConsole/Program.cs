using System.Globalization;

var size = ParseSize(args);
var outerRadius = Math.Max(2, size);
var innerRadius = outerRadius * 0.3819660112501051;
const double xScale = 2.0;

var height = outerRadius * 2 + 5;
var width = (int)Math.Ceiling((outerRadius * 2 + 5) * xScale);
if (width % 2 == 0) width++;

var grid = new char[height, width];
for (var y = 0; y < height; y++)
for (var x = 0; x < width; x++)
    grid[y, x] = ' ';

var cx = width / 2.0;
var cy = height / 2.0;
var vertices = new (int X, int Y)[10];

for (var i = 0; i < 10; i++)
{
    var angle = -Math.PI / 2.0 + i * (Math.PI / 5.0);
    var radius = (i % 2 == 0) ? outerRadius : innerRadius;
    var x = cx + Math.Cos(angle) * radius * xScale;
    var y = cy + Math.Sin(angle) * radius;
    vertices[i] = ((int)Math.Round(x), (int)Math.Round(y));
}

for (var i = 0; i < vertices.Length; i++)
{
    var a = vertices[i];
    var b = vertices[(i + 1) % vertices.Length];
    DrawLine(grid, a.X, a.Y, b.X, b.Y);
}

var top = height - 1;
var bottom = 0;
for (var y = 0; y < height; y++)
{
    if (RowHasStar(grid, y))
    {
        if (y < top) top = y;
        if (y > bottom) bottom = y;
    }
}

if (bottom < top)
{
    return;
}

for (var y = top; y <= bottom; y++)
{
    var line = new string(GetRow(grid, y)).TrimEnd();
    Console.WriteLine(line);
}

static int ParseSize(string[] args)
{
    if (args.Length > 0 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromArg))
    {
        return fromArg;
    }

    var input = Console.ReadLine();
    if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromInput))
    {
        return fromInput;
    }

    return 8;
}

static void DrawLine(char[,] grid, int x0, int y0, int x1, int y1)
{
    var dx = Math.Abs(x1 - x0);
    var dy = -Math.Abs(y1 - y0);
    var sx = x0 < x1 ? 1 : -1;
    var sy = y0 < y1 ? 1 : -1;
    var err = dx + dy;

    while (true)
    {
        Plot(grid, x0, y0);
        if (x0 == x1 && y0 == y1)
        {
            break;
        }

        var e2 = 2 * err;
        if (e2 >= dy)
        {
            err += dy;
            x0 += sx;
        }

        if (e2 <= dx)
        {
            err += dx;
            y0 += sy;
        }
    }
}

static void Plot(char[,] grid, int x, int y)
{
    if (y < 0 || y >= grid.GetLength(0) || x < 0 || x >= grid.GetLength(1))
    {
        return;
    }

    grid[y, x] = '*';
}

static bool RowHasStar(char[,] grid, int y)
{
    for (var x = 0; x < grid.GetLength(1); x++)
    {
        if (grid[y, x] == '*')
        {
            return true;
        }
    }

    return false;
}

static char[] GetRow(char[,] grid, int y)
{
    var row = new char[grid.GetLength(1)];
    for (var x = 0; x < grid.GetLength(1); x++)
    {
        row[x] = grid[y, x];
    }

    return row;
}
