using Eddie.FishingMachine.Core;
using Newtonsoft.Json;
using System.Reflection;

string mod=args.Length>0?args[0]:"src/Eddie.FishingMachine";
int count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
var layout=JsonConvert.DeserializeObject<MachineLayout>(File.ReadAllText(Path.Combine(mod,"assets/layout.json")))!;
layout.Validate();
Check(layout.Width==72 && layout.Height==36,"native body size");
Check(layout.Rods.Length==6,"six rods loaded through SMAPI JSON dependency");
Check(layout.Rods[0].Tip==new PixelPoint(10.5,1.5),"readonly record struct deserializes nonzero tip");
Check(layout.Rods[5].Landing==new PixelPoint(68,23),"record constructor deserializes landing");
Check(layout.BaitOrigin==new PixelPoint(34,18),"bait overlay placement loads");
Check(layout.ReadyOrigin==new PixelPoint(30,5),"ready overlay placement loads");
var cfg=JsonConvert.DeserializeObject<BalanceConfig>(File.ReadAllText(Path.Combine(mod,"fishing-machine.json")))!;cfg.Validate();
Check(cfg.FishCriticalChance==.45 && cfg.Visual.EnableLineSway,"new config reads through Newtonsoft");
var old=JsonConvert.DeserializeObject<BalanceConfig>("{\"BaitPerCycle\":350,\"CycleGameMinutes\":1440,\"Visual\":{\"EnableBobbing\":false}}")!;old.Validate();
Check(old.FishCriticalChance==.45 && old.Visual.EnableLineSway && !old.Visual.EnableBobbing,"old config keeps user settings and receives new defaults");
Check(typeof(Production).Assembly.GetName().Version==new Version(0,6,0,0),"core assembly release version");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{status="passed",assertions=count,note="Uses supplied Newtonsoft.Json; no game process launched."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
