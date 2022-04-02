using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace com.x0
{
    [RequireComponent(typeof(Tilemap))]
    public class MazeGenerator : MonoBehaviour
    {
        private const int Width  = 33;
        private const int Height = 33;
        private static readonly Vector2Int[] Directions = {
            new(-1,  0),
            new( 1,  0),
            new( 0, -1),
            new( 0,  1),
        };

        public Grid Palette;

        private Tilemap _tilemap;
        private TileBase[] _palette;

        private void Start()
        {
            _tilemap = GetComponent<Tilemap>();
            var palette = Palette.GetComponentInChildren<Tilemap>();

            var count = palette.GetUsedTilesCount();
            _palette = new TileBase[count];
            palette.GetUsedTilesNonAlloc(_palette);
        }

        private Cell[,] Generate(Vector2Int from, int num)
        {
            return Generate(from, num, new Unity.Mathematics.Random(1));
        }

        private Cell[,] Generate(Vector2Int from, int num, Unity.Mathematics.Random rand)
        {
            var cells = new Cell[Width, Height];
            var forbiddenEdges = (x: -1, y: -1);
            if (from.x is 0 or Width - 1) {
                forbiddenEdges.x = from.x;
            }
            if (from.y is 0 or Height - 1) {
                forbiddenEdges.y = from.y;
            }
            
            cells[from.x, from.y].Type = CellFlag.Base;
            for (var dy = -1; dy <= 1; dy += 2) {
                for (var dx = -1; dx <= 1; dx += 2) {
                    var x = from.x + dx;
                    var y = from.y + dy;
                    if (IsInBounds(x, y, Width, Height)) {
                        cells[x, y].Type = CellFlag.Base;
                    }
                }
            }

            var xMax = -1;
            var xMin = Width;
            var yMax = -1;
            var yMin = Height;
            
            for (var i = 0; i < num; i++) {
                var forbiddenDir = new Vector2Int();
                var lastDir = new Vector2Int();
                var cursor = from;

                for (int n = 0, threshHold = 500; n < threshHold; n++) {
                    var current = cells[cursor.x, cursor.y];
                    var nextFound = false;
                    
                    foreach (var dir in Directions.OrderBy(d => rand.NextInt(lastDir == d ? -5 : -3, 3))) {
                        var nextCell = cursor + dir;
                        if (!IsInBounds(nextCell.x, nextCell.y, Width, Height)) {
                            continue;
                        }
                        if (dir == forbiddenDir) { 
                            // Debug.Log($"{cursor} -> {nextCell}: no way");
                            continue;
                        }
                        if (cells[nextCell.x, nextCell.y].Type != CellFlag.Ground) {
                            // Debug.Log($"{cursor} -> {nextCell}: not empty cell");
                            continue;
                        }

                        var valid = true;
                        foreach (var adjacentDir in Directions) {
                            var adjacentCell = nextCell + adjacentDir;
                            if (adjacentCell == cursor) {
                                // Debug.Log($"{cursor} -> {nextCell}: {adjacentCell} - going back");
                                continue;
                            }
                            if (!IsInBounds(adjacentCell.x, adjacentCell.y, Width, Height)) {
                                // Debug.Log($"{cursor} -> {nextCell}: {adjacentCell} - oob");
                                continue;
                            }
                            if (cells[adjacentCell.x, adjacentCell.y].Type == CellFlag.Path) {
                                valid = false;
                                break;
                            }
                        }

                        if (valid) {
                            // Debug.Log($"{cursor} -> {nextCell}");
                            nextFound = true;
                            
                            if (current.Distance == 0) {
                                forbiddenDir = cursor - nextCell;
                            }
                            
                            cells[nextCell.x, nextCell.y].Distance = current.Distance + 1;
                            if (xMax < nextCell.x) {
                                xMax = nextCell.x;
                            }
                            if (xMin > nextCell.x) {
                                xMin = nextCell.x;
                            }
                            if (yMax < nextCell.y) {
                                yMax = nextCell.y;
                            }
                            if (yMin > nextCell.y) {
                                yMin = nextCell.y;
                            }

                            if (
                                IsSpawnCell(forbiddenEdges.x, nextCell.x, Width) || 
                                IsSpawnCell(forbiddenEdges.y, nextCell.y, Height)
                            ) {
                                cells[nextCell.x, nextCell.y].Type = CellFlag.Spawn;
                                n = threshHold;
                                break;
                            }
                            
                            cells[nextCell.x, nextCell.y].Type = CellFlag.Path;
                            cursor = nextCell;
                            lastDir = dir;
                        } else {
                            // Debug.LogWarning($"{cursor} -> {nextCell}: invalid direction");
                        }
                    }

                    if (!nextFound) {
                        // Debug.LogWarning($"{cursor}: no path found");
                        // return cells;
                        return Generate(from, num, rand);
                    }
                }
            }

            for (var dy = -1; dy <= 1; dy += 2) {
                for (var dx = -1; dx <= 1; dx += 2) {
                    var x = from.x + dx;
                    var y = from.y + dy;
                    if (IsInBounds(x, y, Width, Height)) {
                        cells[x, y].Type = CellFlag.Ground;
                    }
                }
            }

            if (xMin == 0 && yMin == 0 && xMax == Width - 1 && yMax == Height - 1) {
                return AddObstacles(cells, rand.NextFloat());
            }

            var result  = new Cell[Width, Height];
            var offsetX = (Width - xMax + xMin) / 2;
            var offsetY = (Height - yMax + yMin) / 2;
            for (int y = yMin; y <= yMax; y++) {
                for (int x = xMin; x <= xMax; x++) {
                    result[x + offsetX, y + offsetY] = cells[x, y];
                }
            }
            
            return AddObstacles(result, rand.NextFloat());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsInBounds(int x, int y, int width, int height)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsSpawnCell(int forbiddenEdge, int cellValue, int upperBound)
        {
            if (forbiddenEdge == -1 || cellValue != forbiddenEdge) {
                if (cellValue == 0 || cellValue == upperBound - 1) {
                    return true;
                }
            }
            return false;
        }

        private Cell[,] AddObstacles(Cell[,] cells, float seed)
        {
            var w = cells.GetLength(0);
            var h = cells.GetLength(1);
            
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    if (cells[x, y].Type == CellFlag.Ground) {
                        if (noise.snoise(new float3(x, y, seed)) > .7f) {
                            cells[x, y].Type = CellFlag.Obstacle;
                        }
                    }
                }
            }
            return cells;
        }

        private void Render(Cell[,] cells)
        {
            for (int y = 0, h = cells.GetLength(1), w = cells.GetLength(0); y < h; y++) {
                for (int x = 0; x < w; x++) {
                    var tile = cells[x, y].Type switch {
                        CellFlag.Ground   => _palette[0],
                        CellFlag.Path     => _palette[1],
                        CellFlag.Base     => _palette[2],
                        CellFlag.Spawn    => _palette[3],
                        CellFlag.Obstacle => _palette[4],
                    };
                    _tilemap.SetTile(new Vector3Int(x - w / 2, y - h / 2), tile);
                }
            }
        }


        private struct Cell
        {
            public CellFlag Type;
            public int Distance;
        }

        private enum CellFlag : byte
        {
            Ground   = 0,
            Path     = 1,
            Base     = 2,
            Spawn    = 3,
            Obstacle = 4,
        }
    }
}
