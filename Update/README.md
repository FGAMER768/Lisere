# Installation et mises à jour

Liseré reprend le mécanisme de PresetStore et Unowhy Tools : le programme d'installation et les mises à jour sont séparés.

## Installation

La version installable est publiée dans la dernière release GitHub sous le nom stable `Lisere-Setup.exe`. Le programme d'installation :

- installe Liseré pour le compte Windows courant, dans `%LOCALAPPDATA%\Programs\Lisere` par défaut ; le dossier choisi doit être vide ou contenir une installation existante, car la désinstallation le supprime en entier ;
- ne demande pas de droits administrateur et n'ajoute aucun runtime .NET pour l'application : elle est autonome ;
- embarque lui-même le paquet `Update\Lisere.zip`, donc l'installation ne nécessite pas Internet ;
- crée un raccourci dans le menu Démarrer et, en option, sur le Bureau ;
- enregistre la désinstallation dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Lisere`.

La désinstallation se fait depuis **Paramètres → Applications → Liseré**, ou avec :

```powershell
"%LOCALAPPDATA%\Programs\Lisere\Lisere.exe" --uninstall
```

L'application, ses raccourcis et sa clé de désinstallation sont supprimés après la fermeture de Liseré. Les images, presets et réglages de `%APPDATA%\Lisere` sont conservés.

L'installateur n'est pas encore signé numériquement : Windows peut afficher un avertissement SmartScreen. L'empreinte `Lisere-Setup.exe.sha256` publiée avec la release permet de vérifier le fichier.

Le dossier d'installation doit rester accessible en écriture par l'utilisateur : c'est ce qui permet à Liseré de se mettre à jour sans UAC. Une copie placée dans `C:\Program Files` ne peut pas se mettre à jour toute seule.

## Fonctionnement des mises à jour

L'application :

1. lit `Update/version.txt` depuis la branche `master` ;
2. compare la version publiée à la version de l'assembly ;
3. télécharge le paquet de la dernière release GitHub ;
4. vérifie le SHA-256, contrôle l'archive et extrait les fichiers dans un
   dossier temporaire ;
5. un script `cmd` attend la fermeture de Liseré, copie le paquet, puis relance
   l'application.

Le ZIP n'est pas versionné : il contient l'application autonome, FFmpeg et les
modèles, et pèse environ 160 Mo compressé. Il est joint à une release GitHub, à
l'URL stable `releases/latest/download/Lisere.zip`. Le fichier de version reste
dans le dépôt pour que la vérification soit légère.

Le paquet est **self-contained** : la machine cible n'a besoin d'aucun runtime
.NET. Une installation complète occupe environ 375 Mo, principalement à cause
de l'exécutable autonome et du bundle FFmpeg.

## Publier une version

1. Modifiez `<Version>` dans `src/Lisere/Lisere.csproj`, par exemple `1.0.2`.
2. Publiez le paquet de mise à jour :

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/publish-update.ps1
   ```

   Le script publie l'application en self-contained, vérifie la présence de
   `Lisere.exe` et du dossier `ffmpeg/`, produit `Update/Lisere.zip` et
   `Update/Lisere.zip.sha256`, puis écrit la version dans `Update/version.txt`.

3. Reconstruisez l'installateur avec ce nouveau paquet :

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/build-setup.ps1
   ```

   Le script vérifie que `Update/version.txt` correspond à la version de
   l'application, compile `src/Lisere.Setup` et produit :

   - `artifacts/installer/Lisere-Setup.exe` (nom stable utilisé par le lien de
     téléchargement) ;
   - `artifacts/installer/Lisere-Setup-<version>.exe` (copie versionnée) ;
   - `artifacts/installer/Lisere-Setup.exe.sha256`.

   L'installateur embarque le ZIP : il doit toujours être reconstruit après
   `publish-update.ps1`.

4. Créez une release GitHub dont le tag correspond à la version (par exemple
   `v1.0.2`) et joignez les quatre assets :

   - `Lisere-Setup.exe` ;
   - `Lisere-Setup.exe.sha256` ;
   - `Lisere.zip` ;
   - `Lisere.zip.sha256`.

   La release doit être la plus récente, car les URL de téléchargement
   utilisent `releases/latest`.

5. Committez `Update/version.txt` après le téléversement des assets.

Tant que le ZIP ou son empreinte est absent, la vérification peut annoncer une
version disponible mais l'installation s'arrête proprement avec un message
localisé. Aucun fichier de l'application n'est remplacé avant la validation
complète du paquet.

## Contenu attendu du ZIP

Le ZIP doit contenir `Lisere.exe` à sa racine, le dossier `ffmpeg/` avec
`ffmpeg.exe`, `ffprobe.exe`, les DLL et `manifest.json`, ainsi que les modèles
et ressources copiés par `dotnet publish`. L'extraction refuse les chemins
absolus, les remontées `..`, les entrées trop nombreuses et les archives dont la
taille décompressée dépasse 1,5 Go.

Les réglages utilisateur restent dans `%APPDATA%\Lisere` et ne font pas partie du
paquet.
