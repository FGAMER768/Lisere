using System.Globalization;

namespace Lisere.Setup
{
    internal enum SetupLanguage
    {
        French,
        English
    }

    internal sealed class SetupText
    {
        private readonly bool _english;

        public SetupText(SetupLanguage language)
        {
            _english = language == SetupLanguage.English;
        }

        public string WindowTitle => _english ? "Install Liseré" : "Installer Liseré";
        public string Heading => _english ? "Install Liseré" : "Installer Liseré";
        public string Intro => _english
            ? "A local, self-contained installation for this Windows account."
            : "Une installation locale et autonome, pour ce compte Windows.";
        public string DestinationLabel => _english ? "Installation folder" : "Dossier d'installation";
        public string Browse => _english ? "Browse..." : "Parcourir...";
        public string DestinationNote => _english
            ? "The application is installed for this user only. Administrator rights are not required."
            : "L'application est installée pour cet utilisateur uniquement. Aucun droit administrateur n'est demandé.";
        public string DesktopShortcut => _english ? "Create a Desktop shortcut" : "Créer un raccourci sur le Bureau";
        public string ReadyTitle => _english ? "Ready to install" : "Prêt à installer";
        public string ReadyDetail => _english
            ? "Liseré, FFmpeg and the local models will be copied to the chosen folder."
            : "Liseré, FFmpeg et les modèles locaux seront copiés dans le dossier choisi.";
        public string InstallingTitle => _english ? "Installing Liseré" : "Installation de Liseré";
        public string InstallingReassurance => _english
            ? "Installation runs in the chosen folder. Your images, presets and settings stay in your profile and are not changed."
            : "L'installation se déroule dans le dossier choisi. Vos images, presets et réglages restent dans votre profil et ne sont pas modifiés.";
        public string DoneTitle => _english ? "Installation complete" : "Installation terminée";
        public string DoneDetail => _english
            ? "Liseré is ready. You can launch it now or close this window."
            : "Liseré est prêt. Vous pouvez le lancer maintenant ou fermer cette fenêtre.";
        public string CanceledTitle => _english ? "Installation canceled" : "Installation annulée";
        public string CanceledDetail => _english
            ? "No application files were changed."
            : "Aucun fichier de l'application n'a été modifié.";
        public string ErrorTitle => _english ? "Installation failed" : "L'installation a échoué";
        public string ErrorDetail => _english
            ? "The application could not be installed. Check the destination folder and try again."
            : "L'application n'a pas pu être installée. Vérifiez le dossier de destination et réessayez.";
        public string Install => _english ? "Install" : "Installer";
        public string Installing => _english ? "Installing..." : "Installation...";
        public string Cancel => _english ? "Cancel" : "Annuler";
        public string Close => _english ? "Close" : "Fermer";
        public string Launch => _english ? "Launch Liseré" : "Lancer Liseré";
        public string BrowseTitle => _english ? "Choose an installation folder" : "Choisir un dossier d'installation";
        public string InvalidPath => _english
            ? "Choose a valid folder outside the Windows system directory."
            : "Choisissez un dossier valide, en dehors du dossier système Windows.";
        public string AccessDenied => _english
            ? "This folder cannot be written to. Choose another folder."
            : "Impossible d'écrire dans ce dossier. Choisissez un autre dossier.";
        public string DestinationNotEmpty => _english
            ? "This folder already contains other files. Choose an empty folder or an existing Liseré installation."
            : "Ce dossier contient déjà d'autres fichiers. Choisissez un dossier vide ou une installation existante de Liseré.";
        public string AppRunning => _english
            ? "Liseré is still open. Close it and try again."
            : "Liseré est encore ouvert. Fermez-le puis réessayez.";
        public string Version => _english ? "Version {0}" : "Version {0}";
        public string FinishPath => _english
            ? "Installed in: {0}"
            : "Installé dans : {0}";
        public string SecurityNote => _english
            ? "Your images, presets and settings stay in your user profile. Updates are checked only when you ask for them."
            : "Vos images, presets et réglages restent dans votre profil. Les mises à jour ne sont vérifiées que lorsque vous le demandez.";
        public string CloseDuringInstall => _english
            ? "Cancel the installation before closing the window."
            : "Annulez l'installation avant de fermer la fenêtre.";
        public string SetupFailed => _english
            ? "The setup could not be started."
            : "Le programme d'installation n'a pas pu démarrer.";
        public string PayloadMissing => _english
            ? "The setup is incomplete: application files are missing."
            : "Le programme d'installation est incomplet : les fichiers de l'application sont absents.";
        public string LogLocation => _english
            ? "Details are written to {0}."
            : "Les détails sont écrits dans {0}.";
        public string VersionText(string version) =>
            string.Format(CultureInfo.CurrentCulture, Version, version);
        public string FinishPathText(string path) =>
            string.Format(CultureInfo.CurrentCulture, FinishPath, path);
    }
}
