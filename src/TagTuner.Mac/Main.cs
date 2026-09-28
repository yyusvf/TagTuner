using TagTuner.Mac;

// Ohne Storyboard: Fenster und Menü entstehen im Code (AppDelegate, MainMenu).
// Der Delegate muss darum hier gesetzt werden, bevor die Ereignisschleife läuft.
NSApplication.Init();
NSApplication.SharedApplication.Delegate = new AppDelegate();
NSApplication.Main(args);
