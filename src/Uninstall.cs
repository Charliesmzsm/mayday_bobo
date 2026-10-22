using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
[assembly: AssemblyTitle("Mojo 萝卜桌宠卸载")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]
namespace MojoCarrot.Installation {
    static class Uninstaller {
        [STAThread] static int Main(string[] args){
            Application.EnableVisualStyles();
            try{
                if(args.Length==1&&args[0]=="--worker"){
                    InstallPaths.ClosePet();InstallPaths.Unregister();InstallPaths.RemoveOwned(InstallPaths.Home);
                    MessageBox.Show("Mojo 萝卜桌宠已卸载。\n\n个人设置已保留，如需清除，可删除：\n"+InstallPaths.Data,"卸载完成",MessageBoxButtons.OK,MessageBoxIcon.Information);return 0;
                }
                if(MessageBox.Show("确定卸载 Mojo 萝卜桌宠？\n\n程序会退出并移除，个人设置会保留。","卸载 Mojo 萝卜桌宠",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return 0;
                // Run a temporary copy so the installed uninstaller can be removed while it is running.
                string temporary=Path.Combine(Path.GetTempPath(),"MojoCarrot-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
                string worker=Path.Combine(temporary,"Uninstall.exe");File.Copy(Application.ExecutablePath,worker);
                Process.Start(new ProcessStartInfo(worker,"--worker"){UseShellExecute=true});return 0;
            }catch(Exception ex){MessageBox.Show("卸载未完成：\n"+ex.Message+"\n\n关闭桌宠后可重试。","Mojo 萝卜",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
}
