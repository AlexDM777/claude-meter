using System;
using System.Threading.Tasks;
using System.Windows;
namespace ClaudeMeter {
public partial class Widget {
    string UpdateLabel(){return appUpdate.Available!=null?Lang.T("Доступно обновление")+" "+appUpdate.Available.Version:Lang.T(appUpdate.Status);}
    void UpdateUpdateControls(){if(appUpdate==null)return;if(updateMenu!=null){updateMenu.Header=appUpdate.Busy?Lang.T(appUpdate.Status):appUpdate.Available!=null?Lang.T("Обновить программу")+" · "+appUpdate.Available.Version:Lang.T("Проверить обновления программы");updateMenu.IsEnabled=!appUpdate.Busy;updateMenu.ToolTip=Lang.T(appUpdate.Status);}if(autoUpdateMenu!=null)autoUpdateMenu.IsChecked=autoUpdate;
        if(trayHover!=null){string text=appUpdate.Available!=null||appUpdate.Busy||appUpdate.Status.StartsWith("Не удалось")?UpdateLabel():"";trayHover.SetUpdate(text,appUpdate.Available!=null,appUpdate.Busy);}}
    async Task CheckAppUpdate(bool manual){if(closed||appUpdate.Busy)return;await appUpdate.Check();if(closed)return;UpdateUpdateControls();if(appUpdate.Available!=null&&autoUpdate)await InstallAppUpdate(false);else if(manual&&appUpdate.Available==null)MessageBox.Show(this,Lang.T(appUpdate.Status),Lang.T("Обновить программу"));}
    async Task InstallAppUpdate(bool manual){if(closed||appUpdate.Busy)return;string job=await appUpdate.Download();if(closed)return;if(job==null){if(manual)MessageBox.Show(this,Lang.T(appUpdate.Status),Lang.T("Обновить программу"));return;}if(!manual&&!autoUpdate)return;try{AppUpdate.Launch(job);Quit();}catch{appUpdate.Status="Не удалось обновить программу";UpdateUpdateControls();if(manual)MessageBox.Show(this,Lang.T(appUpdate.Status),Lang.T("Обновить программу"));}}
}
}
