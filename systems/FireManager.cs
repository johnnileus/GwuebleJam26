using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using Godot.Collections;
using Array = System.Array;
using Color = Godot.Color;

public enum CellState : byte{
    Unburnt,
    Burning,
    Burnt,
    Wet,
    Water,
    Forest
}

public struct Cell{
    public CellState State;
    public float Moisture;
    public float BurnTimer;
    public int ParticleSlot;
}


public class FireChunk{
     public Cell[] Current;
     public Cell[] Next;
     public Vector2I ChunkPosition;
     public bool IsOnFire = false;
     public int cellsOnFire;


     public FireChunk(int size, Vector2I chunkPos){
         Current = new Cell[size * size];
         Next = new Cell[size * size];
         ChunkPosition = chunkPos;
     }
}

public partial class FireManager : Node{

    public static FireManager Instance { get; private set; }


    [Export] private bool _isMainMenu = false;
    [Export] private bool _drawGrid = true;
    
    [Export] private float _cellSize;
    [Export] private int _interiorSize;
    private int _gridW, _gridH;
    [Export] private Texture2D _mapImage;

    [Export] private float _baseSpreadChance = .3f;
    [Export] private float _minBurnDurationToSpread = 2f;
    [Export] private float _burnTime = 12f;

    private readonly float _hexRowOffset = Mathf.Sin(Mathf.Pi / 3f);
    
    private FireChunk[] _chunks;
    private List<FireChunk> _activeChunks;
    private Cell[] _padded;

    private FastNoiseLite _noise = new();
    [Export] private float _moistureScale = 1.5f;
    [Export] private float _moistureOffset = .8f;

    private int _totalTicks = 0;
    
    private Vector3 _globalOffset;

    [Export] private PackedScene _testBall;

    [Export] private GameOverUi _gameOverUi;
    private bool _fireHasStarted;   // guards the "no fire at startup = instant win" case
    private bool _gameOver;
    
    //minimap
    [Export] private TextureRect _minimapNode;
    private ImageTexture _minimapTex;
    private Image _minimapImage;




    private static readonly Vector2I[] _evenNeighbors = { // clockwise starting NW
            new(-1, -1),  new(0, -1),
        new(-1, 0),           new(+1, 0),
            new(-1, +1),  new(0, +1)
    };
    private static readonly Vector2I[] _oddNeighbors = {
            new(0, -1),  new(+1, -1),
        new(-1, 0),           new(+1, 0),
            new(0, +1),  new(+1, +1)
    };

    public override void _Ready(){

        Instance = this;
        _gameOver = false;
        Texture2D texture = GD.Load<Texture2D>(_mapImage.GetPath());
        Image image = texture.GetImage();
        _gridW = image.GetWidth() / _interiorSize;
        _gridH = image.GetHeight() / _interiorSize;


        
        _chunks = new FireChunk[_gridW * _gridH];
        
        _globalOffset = new Vector3(_cellSize * _interiorSize * _gridW / 2f, 0, _cellSize * _interiorSize * _gridH / 2f);
        _activeChunks = new List<FireChunk>();

        for (int i = 0; i < _chunks.Length; i++) {
            Vector2I chunkPos = new Vector2I(i % _gridW, i / _gridW);
            _chunks[i] = new FireChunk(_interiorSize, chunkPos);
        }

        _padded = new Cell[(_interiorSize + 2) * (_interiorSize + 2)];

        HexGridRenderer.Instance?.Initialize(_gridW * _gridH * _interiorSize * _interiorSize);

        foreach (var chunk in _chunks){
            for (int y = 0; y < _interiorSize; y++){
                for (int x = 0; x < _interiorSize; x++){
                    int gx = chunk.ChunkPosition.X * _interiorSize + x;
                    int gy = chunk.ChunkPosition.Y * _interiorSize + y;

                    Cell cell = new Cell();
                    cell.ParticleSlot = -1;

                    Color c = image.GetPixel(gx, gy);
                    if (c.B > .1f) {
                        cell.State = CellState.Water;
                    } else if (c.G > .1f) {
                        cell.State = CellState.Forest;
                    } else if (c.R > .1f) {
                        cell.State = CellState.Burning;
                        chunk.IsOnFire = true;
                    } else {
                        cell.State = CellState.Unburnt;
                        float moisture = (_noise.GetNoise2D(gx*2f, gy*2f) + _moistureOffset) * 0.5f;
                        cell.Moisture = moisture * _moistureScale;
                    }

                    chunk.Current[y * _interiorSize + x] = cell;

                    int globalIdx = GlobalCellIndex(chunk.ChunkPosition, y * _interiorSize + x);
                    HexGridRenderer.Instance?.SetCellTransform(globalIdx, CellToWorldPos(chunk.ChunkPosition, x, y));
                    HexGridRenderer.Instance?.SetCellColor(globalIdx, GetColour(cell));
                }
            }
        }

        if (!_isMainMenu) SetupMinimap();



    }

    public override void _Process(double delta){




        var t1 = Time.GetTicksUsec();
        CalculateTick(delta);
        var t2 = Time.GetTicksUsec();


        FireParticlePool.Instance?.AdvanceDyingSlots(delta);



        if (_drawGrid) DrawGrid();

        if (!_isMainMenu) {
            UpdateMinimap();

            DebugUI.Instance?.AddLine($"Time taken: {(t2 - t1) / 1000f}ms, tick: {_totalTicks}");
            DebugUI.Instance?.AddLine($"Active chunks: {_activeChunks.Count}");
            if (FireParticlePool.Instance != null)
                DebugUI.Instance?.AddLine(
                    $"Particle systems: {FireParticlePool.Instance.ActiveSlotCount} / {FireParticlePool.Instance.MaxConcurrentCells}");
        }
    }

    private void SetupMinimap(){
        _minimapImage = Image.CreateEmpty(
            _gridW * _interiorSize,
            _gridH * _interiorSize,
            false,
            Image.Format.Rgba8);
        _minimapTex = ImageTexture.CreateFromImage(_minimapImage);

        if (_minimapNode != null) {
            _minimapNode.Texture = _minimapTex;
        }

    }

    private void UpdateMinimap(){
        
        foreach (var chunk in _chunks) {
            for (int y = 0; y < _interiorSize; y++) {
                for (int x = 0; x < _interiorSize; x++) {
                    _minimapImage.SetPixel(
                        chunk.ChunkPosition.X * _interiorSize + x, 
                        chunk.ChunkPosition.Y * _interiorSize + y,
                        GetColour(chunk.Current[GetChunkIndex(x, y)]));
                }
            }
        }
        _minimapTex.Update(_minimapImage);
    }
    
    public Vector3 GetClickOnPlane(Vector2 mousePos){


        var camera = GetViewport().GetCamera3D();
        if (camera == null) return Vector3.Zero;

        Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
        Vector3 rayDirection = camera.ProjectRayNormal(mousePos);


        if (Mathf.Abs(rayDirection.Y) < 0.0001f) return Vector3.Zero; 

        float t = -rayOrigin.Y / rayDirection.Y;
        if (t < 0) return Vector3.Zero; 


        return rayOrigin + rayDirection * t;
    }
    
    public void IgniteAt(Vector3 globalPos){
        
        var ball = _testBall.Instantiate<Node3D>();
        ball.Position = globalPos;
        AddChild(ball);
        
        Vector3 gridPos = globalPos + _globalOffset;

        int gy = Mathf.RoundToInt(gridPos.Z / (_cellSize * _hexRowOffset));
        int yLocal = ((gy % _interiorSize) + _interiorSize) % _interiorSize;
        float rowShift = yLocal % 2 == 0 ? 0f : _cellSize / 2f;
        int gx = Mathf.RoundToInt((gridPos.X - rowShift) / _cellSize);

        int cx = Mathf.FloorToInt(gx / (float)_interiorSize);
        int cy = Mathf.FloorToInt(gy / (float)_interiorSize);

        FireChunk chunk = GetChunk(cx, cy);
        if (chunk == null) return;

        int idx = yLocal * _interiorSize + (gx - cx * _interiorSize);
        ref Cell cell = ref chunk.Current[idx];

        if (cell.State == CellState.Burning) return;

        cell.State = CellState.Burning;
        cell.ParticleSlot = SpawnParticlesAt(globalPos);
        chunk.IsOnFire = true;

        HexGridRenderer.Instance?.SetCellColor(GlobalCellIndex(chunk.ChunkPosition, idx), GetColour(cell));
    }

    public Vector2I GetChunkAt(Vector3 globalPos){
        Vector3 gridPos = globalPos + _globalOffset;
        int gy = Mathf.RoundToInt(gridPos.Z / (_cellSize * _hexRowOffset));
        int gx = Mathf.RoundToInt(gridPos.X / _cellSize);
        return new Vector2I(Mathf.FloorToInt(gx / (float)_interiorSize),
            Mathf.FloorToInt(gy / (float)_interiorSize));
    }

    public int GetChunkFireCount(int cx, int cy){
        FireChunk c = GetChunk(cx, cy);
        return c?.cellsOnFire ?? 0;
    }

    public Vector3 ChunkOffset(int dx, int dy){
        float w = _interiorSize * _cellSize;
        return new Vector3(dx * w, 0f, dy * w * _hexRowOffset);
    }
    
    private int SpawnParticlesAt(Vector3 globalPos){
        return FireParticlePool.Instance?.AcquireSlot(globalPos) ?? -1;
    }

    public void WetAt(Vector3 globalPos){

        ref Cell cell = ref GetCellAt(globalPos, out Vector2I chunkPos, out int idx);

        if (CanBecomeWet(cell.State)) {
            if (cell.State == CellState.Burning && cell.ParticleSlot >= 0) {
                FireParticlePool.Instance.Release(cell.ParticleSlot);
                cell.ParticleSlot = -1;
            }
            cell.State = CellState.Wet;
            HexGridRenderer.Instance?.SetCellColor(GlobalCellIndex(chunkPos, idx), GetColour(cell));
        }
    }

    public static bool CanBecomeWet(CellState state) =>
        state != CellState.Water &&
        state != CellState.Forest &&
        state != CellState.Burnt &&
        state != CellState.Wet;

    public ref Cell GetCellAt(Vector3 globalPos){
        return ref GetCellAt(globalPos, out _, out _);
    }

    private ref Cell GetCellAt(Vector3 globalPos, out Vector2I chunkPos, out int idx){
        Vector3 gridPos = globalPos + _globalOffset;

        int gy = Mathf.RoundToInt(gridPos.Z / (_cellSize * _hexRowOffset));
        int yLocal = ((gy % _interiorSize) + _interiorSize) % _interiorSize;
        float rowShift = yLocal % 2 == 0 ? 0f : _cellSize / 2f;
        int gx = Mathf.RoundToInt((gridPos.X - rowShift) / _cellSize);

        int cx = Mathf.FloorToInt(gx / (float)_interiorSize);
        int cy = Mathf.FloorToInt(gy / (float)_interiorSize);

        FireChunk chunk = GetChunk(cx, cy);
        if (chunk == null) throw new InvalidOperationException("No chunk at this position");

        idx = yLocal * _interiorSize + (gx - cx * _interiorSize);
        chunkPos = chunk.ChunkPosition;
        return ref chunk.Current[idx];
    }
    private FireChunk GetChunk(int cx, int cy){
        return cx < 0 || cx >= _gridW || cy < 0 || cy >= _gridH ? null : _chunks[cy * _gridW + cx];
    }
    
    
    private int GetChunkIndex(int x, int y) {
        int wrappedX = ((x % _interiorSize) + _interiorSize) % _interiorSize;
        int wrappedY = ((y % _interiorSize) + _interiorSize) % _interiorSize;
        return wrappedY * _interiorSize + wrappedX;
    }
    
    private Cell SampleWorld(Vector2I chunkPos, int x, int y){
        int cx = chunkPos.X,
            cy = chunkPos.Y,
            lx = x,
            ly = y;
        if (x < 0) { cx--; lx = _interiorSize - 1; }
        else if (x >= _interiorSize) { cx++; lx = 0; }
        if (y < 0) { cy--; ly = _interiorSize - 1; }
        else if (y >= _interiorSize) { cy++; ly = 0; }

        FireChunk n = GetChunk(cx, cy);
        return n != null ? n.Current[ly * _interiorSize + lx] : default; 
    }
    
    private void FillPadded(FireChunk chunk){

        for (int y = 0; y < _interiorSize; y++)
            Array.Copy(chunk.Current, y * _interiorSize, _padded, PaddedIdx(0, y), _interiorSize);

        for (int x = -1; x <= _interiorSize; x++){
            _padded[PaddedIdx(x, -1)]         = SampleWorld(chunk.ChunkPosition, x, -1);
            _padded[PaddedIdx(x, _interiorSize)] = SampleWorld(chunk.ChunkPosition, x, _interiorSize);
        }

        for (int y = 0; y < _interiorSize; y++){
            _padded[PaddedIdx(-1, y)]         = SampleWorld(chunk.ChunkPosition, -1, y);
            _padded[PaddedIdx(_interiorSize, y)] = SampleWorld(chunk.ChunkPosition, _interiorSize, y);
        }
    }
    
    private int PaddedIdx(int x, int y) => (y + 1) * (_interiorSize + 2) + (x + 1);
    
    private void CalculateTick(double delta){
        

        _activeChunks.Clear();
        foreach (var chunk in _chunks) {
            if (NeighboursHaveFire(chunk)) _activeChunks.Add(chunk);
        }
        
        foreach (var chunk in _activeChunks) {
            FillPadded(chunk);
            chunk.cellsOnFire = 0;
            bool hasFire = false;
            for (int y = 0; y < _interiorSize; y++) {
                for (int x = 0; x < _interiorSize; x++) {
                    Cell cell = CalculateCell(_padded, chunk.ChunkPosition, x, y, delta);
                    chunk.Next[y * _interiorSize + x] = cell;
                    if (cell.State == CellState.Burning) {
                        hasFire = true;
                        _fireHasStarted = true;
                        chunk.cellsOnFire++;
                    }

                    if (cell.State == CellState.Forest && BurningNeighbour(_padded, x, y)) {
                        TriggerLose();
                    }
                }
            }
        
            chunk.IsOnFire = hasFire;
        }
        foreach (var chunk in _activeChunks)               
            (chunk.Current, chunk.Next) = (chunk.Next, chunk.Current);
        
        if (_fireHasStarted && !_gameOver) {
            bool anyFire = false;
            foreach (var c in _chunks)
                if (c.IsOnFire) { anyFire = true; break; }
            if (!anyFire) TriggerWin();
        }

        _totalTicks++;

    }
    
    private bool BurningNeighbour(Cell[] padded, int x, int y)
    {
        var offsets = y % 2 == 0 ? _evenNeighbors : _oddNeighbors;
        for (int k = 0; k < 6; k++)
            if (padded[PaddedIdx(x + offsets[k].X, y + offsets[k].Y)].State == CellState.Burning)
                return true;
        return false;
    }
    
    private void TriggerWin(){
        if (_isMainMenu) return;
        _gameOverUi.Show("You put out all the fires! :)");
        _gameOver = true;
        
        
    }

    private void TriggerLose(){
        if (_isMainMenu) return;
        _gameOverUi.Show("You let the fire get to the forest! :(");
        _gameOver = true;
    }

    private Cell CalculateCell(Cell[] chunk, Vector2I chunkPos, int x, int y, double delta){
        Cell cell = chunk[PaddedIdx(x, y)];

        switch (cell.State) {
            case CellState.Unburnt:
                return TryIgnite(chunk, chunkPos, x, y, cell, delta);
            case CellState.Burning:
                cell.BurnTimer += (float)delta;

                float t = Mathf.Clamp(cell.BurnTimer / _burnTime, 0f, 1f);
                float intensity = Mathf.Sin(t * Mathf.Pi);
                if (cell.ParticleSlot >= 0)
                    FireParticlePool.Instance?.UpdateCell(cell.ParticleSlot, CellToWorldPos(chunkPos, x, y), intensity, delta);

                if (cell.BurnTimer >= _burnTime) {
                    cell.State = CellState.Burnt;
                    if (cell.ParticleSlot >= 0) {
                        FireParticlePool.Instance?.Release(cell.ParticleSlot);
                        cell.ParticleSlot = -1;
                    }
                }
                HexGridRenderer.Instance?.SetCellColor(GlobalCellIndex(chunkPos, y * _interiorSize + x), GetColour(cell));
                return cell;
            case CellState.Burnt:
            default:
                return cell;
        }

    }


    private Cell TryIgnite(Cell[] chunk, Vector2I chunkPos, int x, int y, Cell cell, double delta){
        if (cell.State == CellState.Burnt) return cell;

        var offsets = y % 2 == 0 ? _evenNeighbors : _oddNeighbors;

        for (int k = 0; k < 6; k++) {

            Cell neighbor = chunk[PaddedIdx(x + offsets[k].X, y + offsets[k].Y)];
            if (neighbor.State != CellState.Burning || neighbor.BurnTimer < _minBurnDurationToSpread) continue;

            if (GD.Randf() < ComputeSpreadChance(cell, delta)) {
                cell.State = CellState.Burning;
                cell.ParticleSlot = SpawnParticlesAt(CellToWorldPos(chunkPos, x, y));
                HexGridRenderer.Instance?.SetCellColor(GlobalCellIndex(chunkPos, y * _interiorSize + x), GetColour(cell));
                return cell;

            }
        }

        return cell;
    }

    private int GlobalCellIndex(Vector2I chunkPos, int localIdx) =>
        (chunkPos.Y * _gridW + chunkPos.X) * (_interiorSize * _interiorSize) + localIdx;

    private Vector3 CellToWorldPos(Vector2I chunkPos, int x, int y){
        float chunkWidth = _interiorSize * _cellSize;
        Vector3 chunkOffset = new Vector3(chunkPos.X * chunkWidth, 0, chunkPos.Y * chunkWidth * _hexRowOffset);
        Vector3 localPos = new Vector3(
            y % 2 == 0 ? x * _cellSize : x * _cellSize + _cellSize / 2f, 0,
            y * _cellSize * _hexRowOffset);
        return localPos + chunkOffset - _globalOffset;
    }
    
    private bool NeighboursHaveFire(FireChunk chunk){
        if (chunk.IsOnFire) return true;
        Vector2I p = chunk.ChunkPosition;
        for (int dy = -1; dy <= 1; dy++){
            for (int dx = -1; dx <= 1; dx++){
                if (dx == 0 && dy == 0) continue;
                FireChunk n = GetChunk(p.X + dx, p.Y + dy);
                if (n != null && n.IsOnFire) return true;
            }
        }
        return false;
    }
    

    
    private float ComputeSpreadChance(Cell target, double delta){
        float chance = _baseSpreadChance * (float) delta;
        chance *= 1f - Mathf.Clamp(target.Moisture, 0f, 1f);
        return Mathf.Clamp(chance, 0f, 1f);
    }
    
    private void DrawGrid(){
        float chunkWidth = _interiorSize * _cellSize;
        foreach (var chunk in _chunks) {
            Vector3 chunkOffset = new Vector3(chunk.ChunkPosition[0] * chunkWidth, 0, chunk.ChunkPosition[1] * chunkWidth * Mathf.Sin(Mathf.Pi/3f));

            for (int y = 0; y < _interiorSize; y++) {
                for (int x = 0; x < _interiorSize; x++) {
                    Cell cell = chunk.Current[GetChunkIndex(x, y)];

                    Vector3 position = new Vector3(y % 2 == 0 ? x * _cellSize : x * _cellSize + _cellSize/2f, 0,  y * _cellSize * _hexRowOffset);
                    Vector3 size = new Vector3(_cellSize, 0.01f,_cellSize);
                    Color colour = GetColour(cell);

                    DrawCell(position + chunkOffset - _globalOffset, size * .9f, colour);
                }
            }
        }
    }

    private Color GetColour(Cell cell){
        if (cell.State == CellState.Unburnt) {
            return new Color(0.15f, 0.6f - cell.Moisture * .6f, 0.1f);
        } else if (cell.State == CellState.Burning) {
            return Colors.Yellow.Lerp(Colors.Red, Mathf.Clamp(cell.BurnTimer / _burnTime, 0f, 1f)); 
        } else if (cell.State == CellState.Burnt) {
            return new Color(0.12f, 0.1f, 0.1f);
        } else if (cell.State == CellState.Water) {
            return Colors.Blue;
        } else if (cell.State == CellState.Forest) {
            return new Color(0, .3f, 0);
        } else if (cell.State == CellState.Wet) {
            return new Color(0.1f, 0.2f , 0.07f);
        }
        else return Colors.Magenta;
        
    }

    private void DrawCell(Vector3 pos, Vector3 size, Color col){
        DebugDraw3D.DrawBox(pos, Quaternion.Identity, size, col, is_box_centered: true);
    }

    
}
