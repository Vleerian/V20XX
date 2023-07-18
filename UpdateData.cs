using SQLite;

[Table("Update_Data")]
public class UpdateData
{
    public int NumNations { get; init; }
    public double MajorLength { get; init; }
    public double TPN_Major { get; init; }
    public double MinorLength { get; init; }
    public double TPN_Minor { get; init; }
}