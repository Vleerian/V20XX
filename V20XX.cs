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
        await Database.ExecuteAsync("CREATE VIEW Raw_Estimates AS SELECT r_1.ID, r_1.Name, hasGovernor, hasPassword, isFrontier, Nation.ID * (SELECT TPN_Major FROM UpdateData) AS MajorEST, MajorACT, Nation.ID * (SELECT TPN_Minor FROM UpdateData) AS MinorEST, MinorACT, Delegate, DelegateAuth, DelegateVotes, Founder, FounderAuth, Embassies, Factbook FROM Nation INNER JOIN (SELECT *, LastMajorUpdate - (SELECT MIN(LastMajorUpdate) FROM Region) AS MajorACT, LastMinorUpdate - (SELECT MIN(LastMinorUpdate) FROM Region WHERE LastMinorUpdate > 0) AS MinorACT FROM Region) AS r_1 ON Nation.Region = r_1.ID GROUP BY Region ORDER BY Nation.ID;");
        await Database.ExecuteAsync("CREATE VIEW Update_Times AS SELECT ID, Name, hasGovernor, hasPassword, isFrontier, strftime('%H:%M:%f', MajorEst, 'unixepoch') as MajorEST, strftime('%H:%M:%f', MajorAct, 'unixepoch') as MajorACT, ROUND(MajorAct - MajorEst, 3) AS MajorVar, strftime('%H:%M:%f', MinorEst, 'unixepoch') as MinorEST, strftime('%H:%M:%f', MinorAct, 'unixepoch') as MinorACT, ROUND(MinorAct - MinorEst, 3) AS MinorVar, Delegate, DelegateAuth, DelegateVotes, Founder, FounderAuth, Embassies, Factbook FROM Raw_Estimates");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
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
