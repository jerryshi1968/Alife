using System;
using System.IO;
using System.Text;
using System.Windows;

using Alife.Function.DeskPet;

namespace Alife.DeskPet;

public partial class App
{
    PetActivity activity = null!;

    protected override async void OnStartup(StartupEventArgs startupEvent)
    {
        try
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            File.Create("pet.log").Close();

            base.OnStartup(startupEvent);

            string[] args = Environment.GetCommandLineArgs();
            string defaultModel = PetModelMetadata.ResolveModelJsonPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot/model"), "Mao");
            string modelPath = args.Length > 1 ? args[1] : defaultModel;
            PetModelMetadata metadata = PetModelMetadata.Load(modelPath);
            MainWindow mainWindow = await DeskPet.MainWindow.Create();
            PetProcess petProcess = new(Console.Out, Console.In); //与客户端的通讯
            PetBridge bridge = new(mainWindow.WebView, metadata); //与前端的通讯

            MainWindow = mainWindow;
            activity = new(petProcess, bridge, metadata, mainWindow);
        }
        catch (Exception e)
        {
            await File.AppendAllTextAsync("pet.log", e + Environment.NewLine);
        }
    }
}
