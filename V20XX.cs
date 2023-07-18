using NSDotnet;
using NSDotnet.Enums;
using NSDotnet.Models;

using Spectre.Console;
using Spectre.Console.Cli;

using SQLite;
using SQLitePCL;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

#region License
/*
V20XX Raiding Suite
Copyright (C) 2022 Vleerian

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
#endregion

internal sealed class V20XX : AsyncCommand<V20XX.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-n|--nation"), Description("The nation using V20XX")]
        public string? Nation { get; init; }

        [CommandOption("-p|--poll-speed"), Description("Poll speed, minimum is 750")]
        public int? PollSpeed { get; init; }

        [CommandOption("-b|--beep"), Description("If you want V20XX to beep when a target updates")]
        public bool? Beep { get; init; }

        [CommandOption("-w|--width"), Description("How wide you want your triggers to be in seconds")]
        public int? Width { get; init; }
        [CommandOption("-s|--switch"), Description("How long to leave between switches")]
        public int? Switch { get; init; }
        [CommandOption("-d|--dump"), Description("Data dump file to use")]
        public string? DataDump { get; init; }
        [CommandOption("-t|--target"), Description("The target to use")]
        public string? Target { get; init; }
        [CommandOption("-m|--minor"), Description("If to be in Major or Minor mode. Default: Major"), DefaultValue(true)]
        public bool Minor { get; init; }
    }

    private SQLiteAsyncConnection Database;
    private Settings settings;
    const string Check = "[green]✓[/]";
    const string Cross = "[red]x[/]";
    const string Arrow = "→";

    string RWG(int a) => a > 0 ? "green" : a == 0 ? "blue" : "red";

    public async Task ProcessDump(string DumpName)
    {
        // Process the dump name
        string DBFile = DumpName.Replace("regions", "data").Replace(".xml.gz", ".db");
        // Open a database connection - if it exists, skip processing the data dump
        Logger.Info("Opening SQLite database");
        bool SkipProcessing = File.Exists(DBFile);
        Database = new SQLiteAsyncConnection(DBFile);
        if(SkipProcessing)
        {
            Logger.Info("Existing database found. Skipping data dump processing.");
            return;
        }

        // If the data dump exists, use it, if not download it
        if(!File.Exists(DumpName)) // Download the datadump if it does not exist
        {
            Logger.Request($"{DumpName} not found - downloading now.");
            DumpName = await NSAPI.Instance.DownloadDataDump(DataDumpType.Regions);
        }
        Logger.Processing("Unzipping data dump.");
        var DataDump = NSDotnet.Helpers.BetterDeserialize<RegionDataDump>(NSAPI.UnzipDump(DumpName));

        bool Current = $"regions.{DateTime.Now.ToString("MM.dd.yyyy")}.xml.gz" == DumpName;
        // Get relevant R/D data
        Logger.Request("Getting Governorless Regions");
        (HttpResponseMessage Response, WorldAPI Data) tmp;
        var Govless  = new string[0];
        var Password  = new string[0];
        var Frontiers = new string[0];
        
        if(Current)
        {
            tmp = await NSAPI.Instance.GetAPI<WorldAPI>("https://www.nationstates.net/cgi-bin/api.cgi?q=regionsbytag;tags=governorless");
            Govless = tmp.Data.Regions.Split(",");

            Logger.Request("Getting Passworded Regions");
            tmp = await NSAPI.Instance.GetAPI<WorldAPI>("https://www.nationstates.net/cgi-bin/api.cgi?q=regionsbytag;tags=password");
            Password = tmp.Data.Regions.Split(",");

            Logger.Request("Getting Frontier Regions");
            tmp = await NSAPI.Instance.GetAPI<WorldAPI>("https://www.nationstates.net/cgi-bin/api.cgi?q=regionsbytag;tags=frontier");
            Frontiers = tmp.Data.Regions.Split(",");
        }

        // Populate the database. This is transaction-alized to make it significantly faster.
        await Database.CreateTableAsync<Region>();
        await Database.CreateTableAsync<Nation>();
        await Database.RunInTransactionAsync(Anon => {
            int nationIndex = 0;
            for(int i = 0; i < DataDump.Regions.Length; i++)
            {
                var reg = DataDump.Regions[i];
                try
                {
                Region temp;
                if(Current)
                    temp = new Region(reg) { 
                        hasGovernor = !Govless.Contains(reg.Name),
                        hasPassword = Password.Contains(reg.Name),
                        isFrontier = Frontiers.Contains(reg.Name)
                    };
                else
                    temp = new Region(reg);
                Anon.Insert(temp);

                foreach(var nation in reg.Nations)
                {
                    Anon.Insert(new Nation(){
                        Name = nation,
                        ID = nationIndex++,
                        Region = i
                    });
                }
                }
                catch(Exception e)
                {
                    Logger.Error("Error Encountered", e);
                }
            }
        });

        Logger.Info("Creating views.");
        await Database.ExecuteAsync("CREATE VIEW Update_Data AS SELECT *, MajorLength / NumNations AS TPN_Major, MinorLength / NumNations AS TPN_Minor FROM (SELECT (SELECT COUNT(*) FROM Nation) AS NumNations, MAX(LastMajorUpdate) - MIN(LastMajorUpdate) AS MajorLength, MAX(LastMinorUpdate) - MIN(LastMinorUpdate) AS MinorLength FROM Region WHERE LastMinorUpdate > 0);");
        await Database.ExecuteAsync("CREATE VIEW Raw_Estimates AS SELECT r_1.ID, r_1.Name, hasGovernor, hasPassword, isFrontier, Nation.ID * (SELECT TPN_Major FROM Update_Data) AS MajorEST, MajorACT, Nation.ID * (SELECT TPN_Minor FROM Update_Data) AS MinorEST, MinorACT, Delegate, DelegateAuth, DelegateVotes, Founder, FounderAuth, Embassies, Factbook FROM Nation INNER JOIN (SELECT *, LastMajorUpdate - (SELECT MIN(LastMajorUpdate) FROM Region) AS MajorACT, LastMinorUpdate - (SELECT MIN(LastMinorUpdate) FROM Region WHERE LastMinorUpdate > 0) AS MinorACT FROM Region) AS r_1 ON Nation.Region = r_1.ID GROUP BY Region ORDER BY Nation.ID;");
        await Database.ExecuteAsync("CREATE VIEW Update_Times AS SELECT ID, Name, hasGovernor, hasPassword, isFrontier, strftime('%H:%M:%f', MajorEst, 'unixepoch') as MajorEST, strftime('%H:%M:%f', MajorAct, 'unixepoch') as MajorACT, ROUND(MajorAct - MajorEst, 3) AS MajorVar, strftime('%H:%M:%f', MinorEst, 'unixepoch') as MinorEST, strftime('%H:%M:%f', MinorAct, 'unixepoch') as MinorACT, ROUND(MinorAct - MinorEst, 3) AS MinorVar, Delegate, DelegateAuth, DelegateVotes, Founder, FounderAuth, Embassies, Factbook FROM Raw_Estimates");
    }

    public async Task<Region> SelectTriggerRegion(string Target, double TriggerWidth)
    {
        // Fetch Update Data
        var Data = await Database.Table<UpdateData>().FirstOrDefaultAsync();
        TriggerWidth = settings.Width ?? Data.TPN_Major;
        double TPN = settings.Minor ? Data.TPN_Minor : Data.TPN_Major;

        Target = settings.Target ?? AnsiConsole.Ask<string>("Please enter your [green]Target[/]: ");
        Logger.Info($"Acquiring target data for {Target}");
        var TargetRegion = await Database.FindWithQueryAsync<Region>("SELECT * FROM Region WHERE Name LIKE ?", Helpers.SanitizeName(Target));
        var TargetNation = await Database.GetAsync<Nation>(N => N.Region == TargetRegion.ID);

        int TriggerIndex = (int)(TargetNation.ID - (TriggerWidth / TPN));
        var TriggerNation = await Database.GetAsync<Nation>(N => N.ID == TriggerIndex);
        var TriggerRegion = await Database.GetAsync<Region>(R => R.ID == TriggerNation.Region);

        AnsiConsole.MarkupLine($"Trigger for [green]{Target}[/] - [yellow]{TriggerRegion.Name}[/]");

        return TriggerRegion;
    }

    public async Task ScanRegion(string Region)
    {
        Region = Helpers.SanitizeName(Region);
        var Target = await Database.GetAsync<Region>(R => R.name == Region);
        var TargetAPI = await NSAPI.Instance.GetRegion(Region);
        StringBuilder Output = new();

        Output.AppendLine($"Report on [yellow]{Target.name}[/]");
        Output.AppendLine($"Raidable {(Target.DelegateHas(Authorities.Executive) && !Target.hasPassword ? Check : Cross)}");
        Output.AppendLine($"Governor: {(Target.hasGovernor ? Check : Cross)}");

        int NetChange = Target.NumNations - TargetAPI.NumNations;
        Output.AppendLine($"Nations: {Target.NumNations} [{RWG(NetChange)}]{Arrow}[/] {TargetAPI.NumNations} (Net {NetChange})");
        
        // Officer reports
        // Calculate the threshholds for invisible vs visible passwords
        int Vis = TargetAPI.NumNations*20;
        int Invis = TargetAPI.NumNations*40;
        if ( TargetAPI.Delegate != null && TargetAPI.Delegate.Trim() != string.Empty)
        {
            var Del = await GetNation(TargetAPI.Delegate);
            Output.AppendLine(await GenNationReport(Del, Vis, Invis, TargetAPI.DelegateAuth));
        }
        foreach(var Officer in TargetAPI.Officers.Where(O=>O.Nation?.ToLower() != "cte"))
        {
            var regionOfficer = await GetNation(Officer.Nation);
            Output.AppendLine(await GenNationReport(regionOfficer, Vis, Invis, Officer.OfficerAuth));
        }

        AnsiConsole.MarkupLine(Output.ToString());
    }

    public async Task<string> GenNationReport(NationAPI Nation, int VisThresh, int InvisThresh, string Auth)
    {
        int influence = (int)Nation.CensusData[CensusScore.Influence].CensusScore;

        bool BC = Auth.Contains("B") || Auth.Contains("X");
        bool Vis = influence >= VisThresh;
        bool Invis = influence >= InvisThresh;

        return $"{Nation.InfluenceLevel.ToUpper()} {Nation.name} - BC: {(BC?Check:Cross)} - WA: {(Nation.IsWA?Check:Cross)} - Endos: {Nation.Endos} - Influence: {influence} - PW:{(Vis?Check:Cross)}{(Invis?Check:Cross)}";

    }

    public async Task<NationAPI> GetNation(string NationName) =>
        (await NSAPI.Instance.GetAPI<NationAPI>($"https://www.nationstates.net/cgi-bin/api.cgi?nation={NationName};q=name+endorsements+influence+wa+census;scale=65+80")).Data;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        this.settings = settings;
        // Set up NSDotNet
        var API = NSAPI.Instance;
        API.UserAgent = $"V20XX/0.1 (By Vleerian, Atagait@hotmail.com)";

        #region UserIdentification
        string? UserNation = settings.Nation;
        if(UserNation == null)
        {
            AnsiConsole.WriteLine("Identify your nation to inform NS Admin who is using it.");
            UserNation = AnsiConsole.Ask<string>("Please provide your [green]nation[/]: ");
        }
        var r = await API.MakeRequest($"https://www.nationstates.net/cgi-bin/api.cgi?nation={Helpers.SanitizeName(UserNation)}");
        if(r.StatusCode != HttpStatusCode.OK)
        {
            Logger.Error("Failed to log in. Ensure your nation exists.");
            return 1;
        }
        NationAPI Nation = Helpers.BetterDeserialize<NationAPI>(await r.Content.ReadAsStringAsync());
        API.UserAgent = $"V20XX/0.1 (By 20XX, Atagait@hotmail.com - In Use by {UserNation})";
        #endregion

        // Data dump shit
        string DataDump = settings.DataDump ?? $"regions.{DateTime.Now.ToString("MM.dd.yyyy")}.xml.gz";
        await ProcessDump(DataDump);

        return 0;
    }
}
