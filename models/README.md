# Dossier des modèles

Ce dossier est le point d'entrée des moteurs neuronaux de Liseré. Il est
découvert automatiquement (dossier de l'application, variable
`LISERE_MODELS`, parents du dossier courant en développement, ou
`%LOCALAPPDATA%\Lisere\models`).

## Contenu

| Fichier | Rôle |
|---|---|
| `catalog.json` | Déclaration des modèles : identifiant, fichier, échelle, noms d'entrée/sortie, taille de tuile, licence, source, empreinte SHA-256. |
| `onnx/super-resolution-10.onnx` | SRCNN sous-pixel ×3 (Shi et al., 2016), ONNX Model Zoo. |
| `onnx/LICENSE` | Apache License 2.0 du modèle SRCNN. |
| `onnx/realesr-general-x4v3.onnx` | Real-ESRGAN général ×4 (SRVGGNetCompact), super-résolution RVB. |
| `onnx/realesrgan-x4plus-anime-6b.onnx` | Real-ESRGAN Anime 6B ×4, spécialisé animation et art digital. |
| `onnx/realesr-animevideov3-x4.onnx` | Real-ESRGAN AnimeVideo v3 ×4, version rapide pour la vidéo. |
| `onnx/LICENSE-real-esrgan` | Licence BSD-3-Clause des poids Real-ESRGAN (général, anime et vidéo). |
| `onnx/1xDeNoise_realplksr_otf_fp32.onnx` | Nettoyage 1× RealPLKSR : débruit sans changer la taille. |
| `onnx/LICENSE-realplksr` | Licence CC-BY-4.0 du modèle RealPLKSR. |
| `onnx/rife49.onnx` | RIFE 4.9 (Practical-RIFE), interpolation d'images, export ONNX communautaire. |
| `rife/LICENSE` | Licence MIT de RIFE / Practical-RIFE. |
| `onnx/face_detection_yunet_2023mar.onnx` | YuNet, détection de visages (Shiqi Yu). |
| `yunet/LICENSE` | Licence MIT de YuNet. |

## Modèle de super-résolution SRCNN

- **Source** : <https://github.com/onnx/models/tree/main/validated/vision/super_resolution/sub_pixel_cnn_2016>
- **Licence** : Apache-2.0.
- **Entrée** : `input`, tenseur `1×1×224×224` (luminance normalisée `[0,1]`).
- **Sortie** : `output`, tenseur `1×1×672×672` (facteur ×3).
- **SHA-256** : `85f36ff88cc504a24af5e0602148bc56a8aa09a58eca8c0da2756f3e8186035e`
- **Taille** : 240 Ko environ.

Liseré applique ce réseau sur le plan de luminance, tuile par tuile, avec un
contexte de 8 pixels. La chroma est agrandie en bicubique, puis l'image est
rééchantillonnée vers l'échelle de sortie demandée (1×, 2× ou 4×).

## Modèle de super-résolution RVB Real-ESRGAN

- **Source** : <https://huggingface.co/jonathanst29/tinier-upscale-models>
  (`realesr-general-x4v3.onnx`), export des poids officiels
  `realesr-general-x4v3` de <https://github.com/xinntao/Real-ESRGAN>.
- **Auteur / licence** : Wang, Xie, Dong & Shan (2021), BSD-3-Clause
  (`onnx/LICENSE-real-esrgan`).
- **Entrée** : `input`, tenseur `1×3×H×W`, RVB planaire dans `[0,1]`, `H` et `W`
  libres (l'export n'impose aucune taille).
- **Sortie** : `output`, tenseur `1×3×4H×4W`, valeurs légèrement hors de `[0,1]`
  avant bornage.
- **SHA-256** : `924ebad6532777303582d4ce7811849b88869231a3ae7093e0f21200249df8d5`
- **Taille** : 4,6 Mo environ.

Liseré découpe l'image en fenêtres de 256 px avec 8 px de contexte, exécute
chaque tuile à sa taille réelle — aucun remplissage — et ne recompose que le
cœur de la prédiction. Le réseau reconstruit donc la couleur avec la netteté,
alors que SRCNN ne traite que la luminance et laisse la chroma au bicubique.
L'image ×4 native est ensuite ramenée à la résolution de sortie demandée
(1×, 2× ou 4×).

`realesr-general-x4v3` est un RRDBNet compact (1,2 M de paramètres), assez
rapide pour l'aperçu en direct. Pour une qualité supérieure, `RealESRGAN_x4plus`
(RRDBNet de 23 blocs, 16,7 M de paramètres, export fp32 d'environ 64 Mo) suit le
même contrat `1×3×H×W → 1×3×4H×4W` : déposer le fichier sous `models/onnx/`,
ajouter une entrée `plane: "rgb"` dans `catalog.json`, et le moteur RVB s'en sert
sans changement de code.

## Modèle Anime 6B (image)

- **Source** : <https://huggingface.co/skillsafe-ai/realesrgan-x4plus-anime-6b>
  (`model.onnx`), export des poids officiels `RealESRGAN_x4plus_anime_6B.pth`
  de <https://github.com/xinntao/Real-ESRGAN> (release v0.2.2.4, empreinte
  amont `f872d837d3c90ed2e05227bed711af5671a6fd1c9f7d7e91c911a61f155e99da`).
- **Auteur / licence** : Xintao Wang, BSD-3-Clause (`onnx/LICENSE-real-esrgan`).
- **Entrée** : `input`, tenseur `1×3×H×W`, RVB planaire dans `[0,1]`, `H` et `W`
  libres.
- **Sortie** : `output`, tenseur `1×3×4H×4W`, facteur ×4.
- **SHA-256** : `9a029dae374b2cd4c3eb2dbbed857e22e6d785687c767aa12828e7356b9fb378`
- **Taille** : 17,1 Mo environ.
- **Architecture** : RRDBNet à 6 blocs, la variante anime officielle de
  Real-ESRGAN.

Le réseau est spécialisé dans les traits, les aplats et les cheveux de
l'animation et de l'art digital. Il tient mieux les hachures et les contours
que le modèle général, au prix d'un rendu de peau lissé : **ne pas l'utiliser
sur des photos**. Coût mesuré sur l'iGPU de référence (DirectML) : environ
**22,5 s par mégapixel source**, soit **20,5 s pour une frame 1280×720 en 4×**.
Il passe par le même `OnnxRgbSuperResolutionEnhancer` que les autres modèles
RVB (tuiles de 256 px, 8 px de contexte) et ne demande aucune classe dédiée.

## Modèle AnimeVideo v3 (vidéo)

- **Source** : <https://huggingface.co/skillsafe-ai/realesr-animevideov3>
  (`model.onnx`), export des poids officiels `realesr-animevideov3.pth` de
  <https://github.com/xinntao/Real-ESRGAN> (release v0.2.5.0, empreinte amont
  `b8a8376811077954d82ca3fcf476f1ac3da3e8a68a4f4d71363008000a18b75d`).
- **Auteur / licence** : Xintao Wang, BSD-3-Clause (`onnx/LICENSE-real-esrgan`).
- **Entrée** : `input`, tenseur `1×3×H×W`, RVB planaire dans `[0,1]`, `H` et `W`
  libres.
- **Sortie** : `output`, tenseur `1×3×4H×4W`, facteur ×4.
- **SHA-256** : `78baa685a1a92cac6e14ab2af2a8ad0ef56fa48629563b1414e3a7acc54d86e2`
- **Taille** : 2,4 Mo environ.
- **Architecture** : SRVGGNetCompact (`num_feat` 64, `num_conv` 16), ×4.

C'est la version rapide, pensée pour les frames d'animation : plus douce que
le modèle image, mais beaucoup plus économe. Coût mesuré (DirectML) : environ
**2,5 s par mégapixel source**, soit **2,3 s pour une frame 1280×720 en 4×**,
1,7× plus rapide que le Real-ESRGAN général x4v3. C'est le moteur à choisir
pour les aperçus et les exports vidéo d'animation ; il passe aussi par
`OnnxRgbSuperResolutionEnhancer`.

### Pourquoi pas le « Digital Art » d'Upscayl ?

Le modèle `digital-art-4x` d'Upscayl a été mesuré sur le même protocole : sa
sortie est à **59,2 dB / SSIM 0,9996** de celle d'Anime 6B. Il n'apporte aucun
détail supplémentaire et la licence de ses poids n'est pas documentée. Les deux
poids ci-dessus, issus des releases officielles Real-ESRGAN, sont donc préférés.

## Modèles locaux

Déposez un manifeste JSON dans `custom/`, à côté de ce fichier, et le modèle
apparaît dans la liste des moteurs, après les modèles livrés :

```json
{
  "id": "mon-modele",
  "displayName": "Mon modèle",
  "family": "restoration",
  "file": "onnx/mon-modele.onnx",
  "sha256": "",
  "scale": 1,
  "inputName": "input",
  "outputName": "output",
  "tileSize": 256,
  "plane": "rgb",
  "license": "local",
  "source": "utilisateur",
  "description": "Débruitage maison, export ONNX de mon propre entraînement."
}
```

- `sha256` vide : le modèle tourne, signalé « Modèle local, empreinte non
  vérifiée » sur la page Image. C'est le cas normal d'un fichier qu'on a
  soi-même exporté.
- `sha256` renseigné mais faux : le modèle est refusé, exactement comme un
  modèle livré altéré.
- `file` est relatif au dossier `models/`, pas au dossier `custom/`.
- Un manifeste illisible est ignoré sans priver les autres du chargement.

## Modèle de nettoyage 1× RealPLKSR

- **Source** : <https://github.com/Phhofm/models/releases/tag/1xDeNoise_realplksr_otf>
  (`1xDeNoise_realplksr_otf_fp32.onnx`), par Philip Hofmann (« Helaman »),
  architecture RealPLKSR (projet neosr).
- **Licence** : CC-BY-4.0 (`onnx/LICENSE-realplksr`), attribution requise :
  Philip Hofmann, modèle `1xDeNoise_realplksr_otf`, entraîné sur le jeu
  Nomos-v2 avec le pipeline realesrgan-otf.
- **Entrée** : `input`, tenseur `1×3×H×W`, RVB planaire dans `[0,1]`, `H` et `W`
  libres.
- **Sortie** : `output`, tenseur `1×3×H×W` — la taille ne change pas, d'où
  l'usage « nettoyage » plutôt qu'« agrandissement ».
- **SHA-256** : `fa7a85412634b30e078299aafc9a9d4074358f1a5abcd8f17a8355715a258c51`
- **Taille** : 29,9 Mo environ.

Le réseau retire le bruit de capteur et une partie des artefacts JPEG en
conservant la texture. Il passe par le même moteur RVB que Real-ESRGAN : tuiles
de 256 px avec 8 px de contexte, puis recomposition du cœur de chaque prédiction.

Coût mesuré (DirectML, photo 900×900) : **52 s** ; en imposant le CPU, 65 s. Ce
modèle est donc réservé à un rendu soigné, pas à un enchaînement rapide : pour
le même pixel, Real-ESRGAN x4v3 prend 3,5 s. Le catalogue déclare ce coût
(`secondsPerMegapixel`) pour que la page Image annonce une durée honnête et
fasse avancer sa barre tuile par tuile.

### Comparaison avec SCUNet

Seul autre débruteur 1× disponible en ONNX à entrée libre, SCUNet
(`scunet_color_real_psnr`, MIT, 73 Mo) a été mesuré sur la même machine et la
même photo : 37 s/Mpx avec des tuiles de 264 px, 24 s/Mpx avec 448 px — soit
**44 s au lieu de 52 s** de bout en bout (≈15 %), pour un fichier 2,4 fois plus
lourd, une peau nettement plus lissée et des sourcils aplatis. Le gain est trop
faible pour le coût visuel : RealPLKSR est conservé.

## Modèle d'interpolation RIFE

- **Source** : <https://huggingface.co/edgetools/rife> (`rife49.onnx`), export
  de RIFE 4.9 / Practical-RIFE.
- **Licence** : MIT (`rife/LICENSE`).
- **Entrées** : `img0` et `img1`, tenseurs RGB planaires `1×3×H×W` dans `[0,1]` ;
  `timestep`, tenseur `1` dans `[0,1]`.
- **Sortie** : `output`, tenseur RGB planaire `1×3×H×W`.
- **SHA-256** : `76e4cef9ab42fa7dd4e8f6e4aba47462051e3faa969e4bca6479784fbab0ac6f`
- **Taille** : 20,5 Mo environ.

RIFE exige des dimensions multiples de 32 : Liseré complète les bords, exécute
l'inférence, puis recadre à la taille d'origine. Le moteur est volontairement
limité au **×2** (une image intermédiaire à `timestep = 0.5` par paire).

## Modèle de détection de visage YuNet

- **Source** : <https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet>
  (`face_detection_yunet_2023mar.onnx`).
- **Auteur / licence** : YuNet, Shiqi Yu : MIT (`yunet/LICENSE`).
- **Entrée** : `input`, tenseur BGR `1×3×640×640`, valeurs 0–255.
- **Sorties** : `cls_{8,16,32}`, `obj_{8,16,32}`, `bbox_{8,16,32}`, `kps_{8,16,32}`.
- **SHA-256** : `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4`
- **Taille** : 227 Ko environ.

Liseré s'en sert uniquement pour localiser les visages sur les photos : la
texture de peau est alors concentrée sur les visages détectés et renforcée,
sans grain ajouté au cuir, aux cheveux ou au fond. Si le modèle manque, la
texture revient au masque de peau global.

Citation : Wu, W., Peng, H., Yu, S. (2023). *YuNet: A Tiny Millisecond-level
Face Detector.* Machine Intelligence Research, 20(5), 656–665.

## DirectML, choix du GPU et repli CPU

Le paquet NuGet `Microsoft.ML.OnnxRuntime.DirectML` fournit à la fois l'API
managée et le provider DirectML. Le `device_id` de DirectML est un index DXGI ;
l'adaptateur 0 est celui qui pilote l'affichage, souvent l'iGPU sur un portable
hybride. Liseré énumère donc les adaptateurs DXGI et retient celui qui possède le
plus de **mémoire vidéo dédiée** : la carte dédiée passe devant l'iGPU, dont la
mémoire est partagée avec la RAM système. Les adaptateurs logiciels
(`Microsoft Basic Render Driver`) sont écartés. `LISERE_DML_ADAPTER` accepte un
index (`0`, `1`, ...) ou un fragment de nom (`RTX 4070`) pour forcer un choix.

La session DirectML désactive les optimisations de motif mémoire et impose
l'exécution séquentielle, comme l'exige ONNX Runtime. Si aucun adaptateur
matériel n'est disponible, ou si la session ne peut pas être créée, Liseré
retombe sur le CPU et affiche la raison. Le moteur expose le provider et le GPU
réellement utilisés (« DirectML · NVIDIA GeForce RTX 4070 ») ; aucune
accélération n'est supposée.

Mesure locale (Iris Xe, RIFE, après chauffe) : **37 ms** en 192×192,
**833 ms** en 1080×1080. Le CPU seul est nettement plus lent.

## Vérification

L'empreinte est contrôlée au chargement du catalogue. Si le fichier est absent
ou modifié, le moteur correspondant apparaît **indisponible** dans l'interface
avec la raison exacte, et aucun rendu ne peut être lancé avec lui.

Pour revérifier manuellement :

```powershell
Get-FileHash models\onnx\super-resolution-10.onnx -Algorithm SHA256
Get-FileHash models\onnx\realesr-general-x4v3.onnx -Algorithm SHA256
Get-FileHash models\onnx\realesrgan-x4plus-anime-6b.onnx -Algorithm SHA256
Get-FileHash models\onnx\realesr-animevideov3-x4.onnx -Algorithm SHA256
Get-FileHash models\onnx\1xDeNoise_realplksr_otf_fp32.onnx -Algorithm SHA256
Get-FileHash models\onnx\rife49.onnx -Algorithm SHA256
Get-FileHash models\onnx\face_detection_yunet_2023mar.onnx -Algorithm SHA256
```

## Ajouter un modèle

1. Déposer le fichier `.onnx` (ou le format pris en charge par un futur moteur)
   sous `models/`.
2. Calculer son empreinte SHA-256.
3. Ajouter une entrée dans `catalog.json` avec `id`, `displayName`, `family`,
   `file`, `sha256`, `scale`, `inputName`, `outputName`, `tileSize`, `plane`,
   `license`, `source`, `description`.
4. Si le contrat d'inférence diffère (entrée RGB, échelle dynamique, plusieurs
   entrées), ajouter une implémentation de `IFrameEnhancer` dans `Lisere.Ai`.

Aucun modèle propriétaire ou non redistribuable ne doit être ajouté ici : les
poids au format `.tz` ne sont ni libres ni redistribuables.
