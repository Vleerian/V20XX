using SQLite;
using SQLitePCL;

/// <summary>
/// This is primarily to control the size of the SQlite db And to ensure
/// that our data models play nice with SQlite-Net
/// </summary>
public class Region
{
    public Region() {}
    public Region(NSDotnet.Models.RegionAPI region)
    {
        this.Name = region.Name;
        this.Delegate = region.Delegate;
        this.DelegateVotes = region.DelegateVotes;
        this.DelegateAuth = AuthorityToShort(region.DelegateAuth);
        this.Founder = region.Founder;
        this.NumNations = region.NumNations;
        this.FounderAuth = AuthorityToShort(region.FounderAuth);
        this.Factbook = region.Factbook;
        this.LastUpdate = (double)region.LastUpdate!;
        this.LastMajorUpdate = (double)region.LastMajorUpdate!;
        this.LastMinorUpdate = (double)region.LastMinorUpdate!;
        this.Embassies = string.Join(",",region.Embassies);
    }
    [PrimaryKey, AutoIncrement]
	public int ID { get; set; }
    public string Name { get; init; }
    [Ignore]
    public string name => NSDotnet.Helpers.SanitizeName(Name);

    public int NumNations { get; init; }
    public string Delegate { get; init; }
    public int DelegateVotes { get; init; }
    public ushort DelegateAuth { get; init; }
    public string Founder { get; init; }
    public ushort FounderAuth { get; init; }
    public string Factbook { get; init; }
    public string Embassies { get; init; }
    public double LastUpdate { get; init; }
    public double LastMajorUpdate { get; init; }
    public double LastMinorUpdate { get; init; }

    public static readonly char[,] AuthorityMap = new char[2,8] {
        {'X', 'W', 'S', 'A', 'B', 'C', 'E', 'P'},
        {'','','','','',' ','@',''}
    };

    public static ushort AuthorityToShort(string Authorities)
    {
        if(Authorities == null || Authorities.Trim() == string.Empty)
            return 0;
        var Auths = AuthorityMap;
        ushort tmp = 0;
        for(int i = 0; i < 8; i++)
        {
            if(Authorities.Contains(Auths[0,i]))
                tmp |= (ushort)Auths[1,i];
        }
        return tmp;
    }

    public bool FounderHas(Authorities authority) => (FounderAuth & (ushort)authority) > 0;
    public bool DelegateHas(Authorities authority) => (DelegateAuth & (ushort)authority) > 0;
    public bool hasPassword { get; init; }
    public bool hasGovernor { get; init; }
    public bool isFrontier { get; init; }
}