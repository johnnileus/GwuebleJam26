using Godot;
using System;
using System.Diagnostics;

public enum CellState : byte{
    Unburnt,
    Burning,
    Burnt
}

public struct Cell{
    public CellState State;
    public float Fuel;
    public float Moisture;
    public float BurnTimer;

}

public partial class FireManager : Node{

    [Export] private float _cellSize;
    [Export] private int Width;
    [Export] private int Height;

    [Export] private float _baseSpreadChance = .001f;
    [Export] private float _fuelBurnRate = 2f;
    [Export] private float _maxBurnDuration = 15f;
    [Export] private float _startingFuel = 15f;


    public Cell[] Current;
    public Cell[] Next;


    public override void _Ready(){
        Current = new Cell[Width * Height];
        Next = new Cell[Width * Height];
        
        for (int i = 0; i < Current.Length; i++) {
            Current[i].State = CellState.Unburnt;
            Current[i].Fuel = _startingFuel;
            Current[i].Moisture = 0f;
        }
        
        Current[0].State = CellState.Burning;
    }


    public override void _Process(double delta){
        CalculateTick(delta);
        DrawGrid();
    }
    
    public int GetIndex(int x, int y) {
        int wrappedX = ((x % Width) + Width) % Width;
        int wrappedY = ((y % Height) + Height) % Height;
        return wrappedY * Width + wrappedX;
    }
    private void CalculateTick(double delta){
        
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                Next[GetIndex(x, y)] = CalculateCell(x, y, delta);
            }
        }

        (Current, Next) = (Next, Current);
    }

    private Cell CalculateCell(int x, int y, double delta){
        Cell cell = Current[GetIndex(x, y)];

        switch (cell.State) {
            case CellState.Unburnt:
                return TryIgnite(x, y, cell);
            case CellState.Burning:
                cell.BurnTimer += (float)delta;
                cell.Fuel -= _fuelBurnRate * (float)delta;

                if (cell.Fuel <= 0f || cell.BurnTimer >= _maxBurnDuration)
                {
                    cell.State = CellState.Burnt;
                    cell.Fuel = 0f;
                }
                return cell;
            case CellState.Burnt:
            default:
                return cell;
        }

    }
    
    private Cell TryIgnite(int x, int y, Cell cell){
        if (cell.Fuel <= 0f) return cell;

        for (int i = -1; i <= 1; i++) {
            for (int j = -1; j <= 1; j++) {
                if (i == 0 && j == 0) continue;

                int nx = x + j;
                int ny = y + i;
                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;

                Cell neighbor = Current[GetIndex(nx, ny)];
                if (neighbor.State != CellState.Burning) continue;

                float chance = ComputeSpreadChance(j, i, cell);
                if (GD.Randf() < chance) {
                    cell.State = CellState.Burning;
                    return cell;
                }
            }
        }

        return cell;
    }
    
    private float ComputeSpreadChance(int dx, int dy, Cell target){
        float chance = _baseSpreadChance;
        
        bool isDiagonal = dx != 0 && dy != 0;
        if (isDiagonal) chance *= 0.7f;

        chance *= (1f - Mathf.Clamp(target.Moisture, 0f, 1f));

        return Mathf.Clamp(chance, 0f, 1f);
    }
    
    private void DrawGrid(){
        for (int y = 0; y < Height; y++) {
            for (int x = 0; x < Width; x++) {
                Cell cell = Current[GetIndex(x, y)];

                Vector3 position = new Vector3(x * _cellSize, 0,  y * _cellSize);
                Vector3 size = new Vector3(_cellSize, 0.01f,_cellSize);
                Color colour = StateToColour(cell);

                DrawCell(position, size * .9f, colour);
            }
        }
    }
    private Color StateToColour(Cell cell) => cell.State switch
    {
        CellState.Unburnt  => new Color(0.15f, 0.5f + cell.Fuel / 24f, 0.1f),
        CellState.Burning  => Colors.OrangeRed.Lerp(Colors.Yellow, cell.Fuel / 12f),
        CellState.Burnt => new Color(0.12f, 0.1f, 0.1f), _ => Colors.Magenta
    };

    private void DrawCell(Vector3 pos, Vector3 size, Color col){
        
        DebugDraw3D.DrawBox(pos, Quaternion.Identity, size, col);
        

    }
    
    
}
