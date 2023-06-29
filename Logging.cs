using System.Diagnostics;

using Spectre.Console;

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
public class LogLevel
{
    public string LevelName  { get; init; }
    public string ShortName  { get; init; }
    public string Color      { get; init; }
    public int    Threshhold { get; init; }

    private LogLevel(string levelname, string shortname, string color, int threshhold )
    {
        LevelName = levelname; 
        ShortName = shortname;
        Color = color;
        Threshhold = threshhold;
    }

    public static LogLevel Fatal        = new ("Fatal",      "FTL", "red", 10000);
    public static LogLevel Error        = new ("Error",      "ERR", "red", 9000);
    public static LogLevel Warning      = new ("Warning",    "WRN", "yellow", 8000);
    public static LogLevel Info         = new ("Info",       "INF", "cyan", 5000);
    public static LogLevel Request      = new ("Request",    "REQ", "magenta", 4000);
    public static LogLevel Processing   = new ("Processing", "PRC", "green", 3000);
    public static LogLevel Done         = new ("Done",       "FIN", "green", 3000);
    public static LogLevel Sleeping     = new ("Sleeping",   "SLP", "blue", 1000);

}

public static class Logger
{
    private static readonly string AssemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;

    private static readonly Stopwatch Watch = new();
    public static void Log(string message, LogLevel type, Exception? e = null)
    {
        if(!Watch.IsRunning)
            Watch.Start();

        AnsiConsole.Write($"{Watch.ElapsedMilliseconds.ToString().PadLeft(4, '0')} {AssemblyName} [");
        AnsiConsole.Markup($"[{type.Color}]{type.ShortName}[/]");
        AnsiConsole.Write($"] - {message}\n");

        if(e != null)
        {
            AnsiConsole.MarkupLine($"[red]{e.GetType().ToString()}[/]");
            AnsiConsole.WriteLine(e.ToString());
        }
    }

    public static void Fatal(string message) => Log(message, LogLevel.Fatal);
    public static void Fatal(string message, Exception e) => Log(message, LogLevel.Fatal, e);

    public static void Error(string message) => Log(message, LogLevel.Error);
    public static void Error(string message, Exception e) => Log(message, LogLevel.Error, e);

    public static void Warning(string message) => Log(message, LogLevel.Warning);
    public static void Warning(string message, Exception e) => Log(message, LogLevel.Warning, e);

    public static void Info(string message) => Log(message, LogLevel.Info);
    public static void Info(string message, Exception e) => Log(message, LogLevel.Info, e);

    public static void Request(string message) => Log(message, LogLevel.Request);
    public static void Request(string message, Exception e) => Log(message, LogLevel.Request, e);

    public static void Processing(string message) => Log(message, LogLevel.Processing);
    public static void Processing(string message, Exception e) => Log(message, LogLevel.Processing, e);

    public static void Done(string message) => Log(message, LogLevel.Done);
    public static void Done(string message, Exception e) => Log(message, LogLevel.Done, e);

    public static void Sleeping(string message) => Log(message, LogLevel.Sleeping);
    public static void Sleeping(string message, Exception e) => Log(message, LogLevel.Sleeping, e);
}