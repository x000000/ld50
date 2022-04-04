using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

namespace com.x0
{
    [RequireComponent(typeof(Tilemap))]
    public class MazeGenerator : MonoBehaviour
    {
        private const float ZombieSpeed = .6f;
        private const int EnemyPrice = 1;
        private const int TowerCost  = 10;
        private const int Width  = 21;
        private const int Height = 21;
        private static readonly int DirectionParam = Animator.StringToHash("Direction");
        private static readonly Vector3 VisualOffset = new(Width / 2 - .5f, Height / 2 - .5f);
        private static readonly Vector2Int[] Directions = {
            new( 0,  1),
            new(-1,  0),
            new( 0, -1),
            new( 1,  0),
        };

        public Camera Camera;
        public PlayerInput PlayerInput;
        public TMP_Text MoneyLabel;
        public GameObject WastedScreen;
        
        public Grid Palette;
        public Object ZombieTemplate;
        
        public Button GunTowerButton;
        public Button AoeTowerButton;
        public Button SlowTowerButton;
        public Button CancelButton;
        
        public Object GunTowerTemplate;
        public Object AoeTowerTemplate;
        public Object SlowTowerTemplate;

        private Button[] ActionButtons => new[] { GunTowerButton, AoeTowerButton, SlowTowerButton };

        private Tilemap _tilemap;
        private Dictionary<Tile, TileBase> _palette;
        private Random _rand;
        private Cell[,] _cells;
        private SpawnCell[] _spawnCells;
        private Rect _baseRect;
        private bool _dirty;
        private float _startTime;
        private float _lastTime;
        private int _money;
        private bool _dead;

        private PlacementContext _placementCtx;
        private InputAction _pointAction;
        private InputAction _clickAction;

        private readonly LinkedList<Zombie> _zombies = new();

        public uint Seed;

        public void DisposeLevel()
        {
            SceneManager.activeSceneChanged += OnSceneChanged;
            SceneManager.LoadScene(0);
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            
            foreach (var root in newScene.GetRootGameObjects()) {
                var menu = root.GetComponentInChildren<MainMenu>();
                if (menu != null) {
                    menu.Input.text = Seed.ToString();
                    break;
                }
            }
        }

        public void Init(uint seed)
        {
            Debug.Log("Level initialized with seed " + seed);
            _rand = new Random(Seed = seed);
            Generate();
            _dirty = true;
        }

        private void Awake()
        {
            _tilemap = GetComponent<Tilemap>();
            var palette = Palette.GetComponentInChildren<Tilemap>();

            var count = palette.GetUsedTilesCount();
            var tiles = new TileBase[count];
            palette.GetUsedTilesNonAlloc(tiles);

            _palette = new Dictionary<Tile, TileBase>(count);
            foreach (var tile in tiles) {
                if (Enum.TryParse(tile.name, true, out Tile val)) {
                    _palette.Add(val, tile);
                } else {
                    Debug.LogError($"Failed to parse tile: {tile.name}");
                }
            }
        }

        private void OnEnable()
        {
            _pointAction = PlayerInput.actions["Point"];
            _clickAction = PlayerInput.actions["Click"];
            SlowTower.Affected += OnEnemySlowChanged;
        }

        private void OnDisable()
        {
            SlowTower.Affected -= OnEnemySlowChanged;
            SlowTower.Flush();
        }

        private void OnEnemySlowChanged(object sender, bool slowed)
        {
            var tr = (Transform) sender;
            for (var node = _zombies.First; node != null; node = node.Next) {
                if (node.Value.Transform == tr) {
                    var value = node.Value;
                    value.SpeedMul = slowed ? .6f : 1f;
                    node.Value = value;
                    return;
                }
            }
        }

        private void Start()
        {
            Init(Seed);
            _lastTime = _startTime = Time.fixedTime;
            
            GunTowerButton.onClick.AddListener(() => PlaceTower(GunTowerTemplate));
            AoeTowerButton.onClick.AddListener(() => PlaceTower(AoeTowerTemplate));
            SlowTowerButton.onClick.AddListener(() => PlaceTower(SlowTowerTemplate));
            CancelButton.onClick.AddListener(CancelPlacement);
            
            UpdateMoney(TowerCost * 2);
        }

        private void Update()
        {
            if (_dead) {
                return;
            }
            
            if (_dirty) {
                _dirty = false;
                Render();
            }

            var dt = Time.deltaTime;
            for (var node = _zombies.First; node != null; node = node.Next) {
                var zombie = node.Value;
                var newPos = zombie.Transform.position + zombie.Direction * dt * ZombieSpeed * zombie.Speed * zombie.SpeedMul;

                if ((newPos - zombie.Target).sqrMagnitude < .0001f) {
                    var cellPos = newPos + VisualOffset;
                    var x = (int) Math.Round(cellPos.x); 
                    var y = (int) Math.Round(cellPos.y);
                    var cell = _cells[x, y];
                    
                    // Debug.Log($"({x}, {y}) {cell.Type}: {cell.Direction}");
                    newPos = new Vector3(Mathf.Ceil(newPos.x) - .5f, Mathf.Ceil(newPos.y) - .5f);
                    var dir = new Vector3(cell.Direction.x, cell.Direction.y);
                    if (zombie.Direction != dir) {
                        zombie.Animator.SetInteger(DirectionParam, Array.IndexOf(Directions, cell.Direction));
                        zombie.Direction = dir;
                    }
                    zombie.Target = newPos + zombie.Direction;
                    node.Value = zombie;
                }

                zombie.Transform.position = newPos;

                if (_baseRect.Contains(newPos)) {
                    _dead = true;
                    CancelPlacement();
                    WastedScreen.SetActive(true);
                    return;
                }
            }

            if (_placementCtx != null) {
                var cell = ScreenToCell(_pointAction.ReadValue<Vector2>());
                _placementCtx.OnMove?.Invoke(cell);
                
                if (_clickAction.WasPerformedThisFrame()) {
                    _placementCtx.OnSelect?.Invoke(cell);
                }
            }
        }

        private void FixedUpdate()
        {
            if (_dead) {
                return;
            }
            
            var t = Time.fixedTime;
            if (t - _lastTime > 1) {
                var count = Mathf.Pow(t - _startTime, 2) * .0002f;
                for (int i = 0, num = _spawnCells.Length; i < count; i++) {
                    var zombie = (GameObject) Instantiate(ZombieTemplate);
                    var tr = zombie.transform;
                    var spawn = _spawnCells[_rand.NextInt(num)];
                    var target = new Vector3(spawn.Location.x, spawn.Location.y) - VisualOffset;
                    tr.position = target;
                    
                    var anim = zombie.GetComponent<Animator>();
                    anim.SetInteger(DirectionParam, Array.IndexOf(Directions, spawn.Direction));

                    var dir = new Vector3(spawn.Direction.x, spawn.Direction.y);
                    var node = _zombies.AddLast(new Zombie {
                        Animator  = anim,
                        Transform = tr,
                        Direction = dir,
                        Target    = target + dir,
                        Speed     = _rand.NextFloat(.8f, 1.2f),
                        SpeedMul  = 1.0f,
                    });
                    zombie.GetComponent<IEnemy>().Dies += _ => {
                        UpdateMoney(_money + EnemyPrice);
                        _zombies.Remove(node);
                    };
                }
                _lastTime = t;
            }
        }

        private void Generate()
        {
            _ = _rand.NextInt();
            
            var edge = _rand.NextInt(0, 5);
            var num = edge == 0 ? _rand.NextInt(3, 5) : _rand.NextInt(2, 4);
            var offset = _rand.NextInt(-2, 3);
            var basePos = edge switch {
                1 => new Vector2Int(Width / 2 + offset, Height - 1),
                2 => new Vector2Int(                 0, Height / 2 + offset),
                3 => new Vector2Int(Width / 2 + offset, 0),
                4 => new Vector2Int(         Width - 1, Height / 2 + offset),
                _ => new Vector2Int(Width / 2 + offset, Height / 2 + _rand.NextInt(-2, 3)),
            };
            
            _cells = Generate(ref basePos, num, out _spawnCells);
            
            var worldPos = _tilemap.CellToWorld((Vector3Int) basePos - new Vector3Int(Width / 2, Height / 2));
            _baseRect = new Rect(worldPos, _tilemap.cellSize);
        }
        
        private Cell[,] Generate(ref Vector2Int from, int num, out SpawnCell[] spawnCells)
        {
            var cells = new Cell[Width, Height];
            spawnCells = new SpawnCell[num];
            
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
            var lockX = false;
            var lockY = false;
            
            for (var i = 0; i < num; i++) {
                var forbiddenDir = new Vector2Int();
                var lastDir = new Vector2Int();
                var cursor = from;

                for (int n = 0, threshHold = 500; n < threshHold; n++) {
                    var current = cells[cursor.x, cursor.y];
                    var nextFound = false;
                    
                    foreach (var dir in Directions.OrderBy(d => _rand.NextInt(lastDir == d ? -5 : -3, 4))) {
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
                            cells[nextCell.x, nextCell.y].Direction = -dir;
                            
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

                            if (IsSpawnCell(forbiddenEdges.x, nextCell.x, Width)) {
                                lockX = true;
                                cells[nextCell.x, nextCell.y].Type = CellFlag.Spawn;
                                spawnCells[i] = new SpawnCell {
                                    Location  = nextCell + dir,
                                    Direction = -dir,
                                };
                                n = threshHold;
                                break;
                            }

                            if (IsSpawnCell(forbiddenEdges.y, nextCell.y, Height)) {
                                lockY = true;
                                cells[nextCell.x, nextCell.y].Type = CellFlag.Spawn;
                                spawnCells[i] = new SpawnCell {
                                    Location  = nextCell + dir,
                                    Direction = -dir,
                                };
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
                        return Generate(ref from, num, out spawnCells);
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
                return AddObstacles(cells, _rand.NextFloat());
            }

            var offsetX = lockX ? 0 : (Width - xMax + xMin) / (xMin == 0 ? -2 : 2);
            var offsetY = lockY ? 0 : (Height - yMax + yMin) / (yMin == 0 ? -2 : 2);
            if (offsetX == 0 && offsetY == 0) {
                return AddObstacles(cells, _rand.NextFloat());
            }
            
            var result = new Cell[Width, Height];
            for (int y = yMin; y <= yMax; y++) {
                for (int x = xMin; x <= xMax; x++) {
                    result[x - offsetX, y - offsetY] = cells[x, y];
                }
            }

            for (int i = 0; i < spawnCells.Length; i++) {
                spawnCells[i].Location -= new Vector2Int(offsetX, offsetY);
            }

            from.x -= offsetX;
            from.y -= offsetY;
            return AddObstacles(result, _rand.NextFloat());
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

        private void Render()
        {
            for (int y = 0, h = _cells.GetLength(1), w = _cells.GetLength(0); y < h; y++) {
                for (int x = 0; x < w; x++) {
                    var tile = _cells[x, y].Type switch {
                        CellFlag.Ground => GetGroundTile(x, y),
                        CellFlag.Path or CellFlag.Spawn => GetPathTile(x, y, w, h),
                        CellFlag.Obstacle => GetObstacleTile(x, y),
                        CellFlag.Base => _palette[Tile.Base],
                    };
                    _tilemap.SetTile(new Vector3Int(x - w / 2, y - h / 2), tile);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector3Int ScreenToCell(Vector2 screenPos)
        {
            var worldPos = Camera.ScreenToWorldPoint(screenPos);
            return _tilemap.WorldToCell(worldPos) - _tilemap.origin;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector3 CellToWorld(Vector3Int cellPos)
        {
            return _tilemap.CellToWorld(cellPos) + _tilemap.origin + _tilemap.cellSize / 2;
        }
        
        private TileBase GetGroundTile(int x, int y)
        {
            var r = _rand.NextInt(100);
            if (r < 30) {
                return _palette[Tile.Ground_3];
            }
            if (r < 60) {
                return _palette[Tile.Ground_2];
            }
            return _palette[Tile.Ground_1];
        }

        private TileBase GetObstacleTile(int x, int y)
        {
            var r = _rand.NextInt(100);
            if (r < 50) {
                return _palette[Tile.Obstacle_1];
            }
            if (r < 85) {
                return _palette[Tile.Obstacle_2];
            }
            return _palette[Tile.Obstacle_3];
        }

        private TileBase GetPathTile(int x, int y, int w, int h)
        {
            var d = (
                t: y >= h - 1 ? CountAdjacentPaths(x, y, w, h) <= 1 : IsPath(x, y + 1),
                l: x <= 0     ? CountAdjacentPaths(x, y, w, h) <= 1 : IsPath(x - 1, y),
                b: y <= 0     ? CountAdjacentPaths(x, y, w, h) <= 1 : IsPath(x, y - 1),
                r: x >= w - 1 ? CountAdjacentPaths(x, y, w, h) <= 1 : IsPath(x + 1, y)
            );
            return d switch {
                (true, false, true, false) => _palette[Tile.Path_V],
                (false, true, false, true) => _palette[Tile.Path_H],
                (true, true, false, false) => _palette[Tile.Path_BR],
                (false, false, true, true) => _palette[Tile.Path_TL],
                (false, true, true, false) => _palette[Tile.Path_TR],
                (true, false, false, true) => _palette[Tile.Path_BL],
                
                // bottom left
                (true, true, true, false) when x == 0 && y == 0 => _palette[Tile.Path_H],
                (false, true, true, true) when x == 0 && y == 0 => _palette[Tile.Path_V],
                
                // bottom right
                (true, false, true, true) when x == w - 1 && y == 0 => _palette[Tile.Path_V],
                (false, true, true, true) when x == w - 1 && y == 0 => _palette[Tile.Path_H],
                
                // top left
                (true, true, true, false) when x == 0 && y == h - 1 => _palette[Tile.Path_V],
                (true, true, false, true) when x == 0 && y == h - 1 => _palette[Tile.Path_H],
                
                // top right
                (true, true, false, true) when x == w - 1 && y == h - 1 => _palette[Tile.Path_H],
                (true, false, true, true) when x == w - 1 && y == h - 1 => _palette[Tile.Path_V],
                
                _ => _palette[Tile.Path] ?? throw new ArgumentOutOfRangeException(nameof(d), d, "Invalid path combination"),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsPath(int x, int y)
        {
            return _cells[x, y].Type is CellFlag.Path or CellFlag.Base or CellFlag.Spawn;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsFree(int x, int y)
        {
            var cell = _cells[x, y];
            return cell.Type is CellFlag.Ground && cell.Placement == null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CountAdjacentPaths(int x, int y, int w, int h)
        {
            var count = 0;
            if (IsInBounds(x - 1, y, w, h) && IsPath(x - 1, y)) count++;
            if (IsInBounds(x + 1, y, w, h) && IsPath(x + 1, y)) count++;
            if (IsInBounds(x, y - 1, w, h) && IsPath(x, y - 1)) count++;
            if (IsInBounds(x, y + 1, w, h) && IsPath(x, y + 1)) count++;
            return count;
        }

        private void UpdateMoney(int newMoney)
        {
            MoneyLabel.text = "$" + newMoney;

            if ((_money >= TowerCost) != (newMoney >= TowerCost)) {
                foreach (var button in ActionButtons) {
                    button.interactable = newMoney >= TowerCost;
                }
            }

            _money = newMoney;
        }

        private void ToggleButtons(bool activateActions)
        {
            CancelButton.gameObject.SetActive(!activateActions);
            foreach (var button in ActionButtons) {
                button.gameObject.SetActive(activateActions);
            }
        }
        
        private void CancelPlacement()
        {
            if (_placementCtx != null) {
                Destroy(_placementCtx.Target);
                _placementCtx = null;
            }
            ToggleButtons(true);
        }

        private void PlaceTower(Object towerTemplate)
        {
            var tower = (GameObject) Instantiate(towerTemplate);
            tower.SetActive(false);

            _placementCtx = new PlacementContext {
                Target = tower,
                OnMove = pos => {
                    if (IsInBounds(pos.x, pos.y, Width, Height) && IsFree(pos.x, pos.y)) {
                        tower.transform.position = CellToWorld(pos);
                        tower.SetActive(true);
                    } else {
                        tower.SetActive(false);
                    }
                },
                OnSelect = pos => {
                    if (IsInBounds(pos.x, pos.y, Width, Height) && IsFree(pos.x, pos.y)) {
                        _placementCtx = null;
                        UpdateMoney(_money - TowerCost);   
                        ToggleButtons(true);
                        tower.transform.position = CellToWorld(pos);
                        tower.GetComponent<TowerBase>().enabled = true;
                        tower.SetActive(true);
                    }
                },
            };
            ToggleButtons(false);
        }

        
        private class PlacementContext
        {
            public GameObject Target;
            public Action<Vector3Int> OnMove;
            public Action<Vector3Int> OnSelect;
        }

        private struct Cell
        {
            public CellFlag Type;
            public int Distance;
            public Vector2Int Direction;
            public GameObject Placement;
        }
        
        private struct SpawnCell
        {
            public Vector2Int Location;
            public Vector2Int Direction;
        }
        
        private struct Zombie
        {
            public Animator Animator;
            public Transform Transform;
            public Vector3 Direction;
            public Vector3 Target;
            public float Speed;
            public float SpeedMul;
        }

        private enum CellFlag : byte
        {
            Ground   = 0,
            Path     = 1,
            Base     = 2,
            Spawn    = 3,
            Obstacle = 4,
        }

        private enum Tile
        {
            Ground_1,
            Ground_2,
            Ground_3,
            Obstacle_1,
            Obstacle_2,
            Obstacle_3,
            Path_H,
            Path_V,
            Path_TL,
            Path_BL,
            Path_TR,
            Path_BR,
            Path,
            Base,
            Spawn,
        }
    }
}
