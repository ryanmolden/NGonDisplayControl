namespace Demo
{
    using System;
    using System.Windows;

    public class HostApp
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application app = new Application();

            app.Run(new MainWindow());
        }
    }
}