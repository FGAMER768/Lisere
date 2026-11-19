# Test images

The files in this folder are copied next to the test assembly and are used by
the image, preset and face-detection tests.

## Third-party images

- `face.jpg`: official portrait of President Barack Obama, photographed by
  Pete Souza for the Obama-Biden transition (2009), licensed under
  [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), attribution
  Pete Souza. Source:
  [Wikimedia Commons, File:Official portrait of Barack Obama.jpg](https://commons.wikimedia.org/wiki/File:Official_portrait_of_Barack_Obama.jpg).
  The copy used here is cropped and resized for the YuNet test.

- `preset-photo.jpg`: Blue Lagoon, Iceland, by Jeff Sheldon, distributed by
  [Lorem Picsum](https://picsum.photos/id/512) under its Unsplash licence
  (Picsum ID 512; original: [Unsplash](https://unsplash.com/photos/jTeQavJjBDs)).
  The file is the exact 320 x 213 Picsum crop and is used by the colour-preset
  regression test.

## Generated fixtures

The other images (`alpha.png`, `cmyk.jpg`, `gray16.png`, `image.webp`,
`indexed.gif`, `odd.bmp`, `orientation6*`, `progressive.jpg`) are synthetic
fixtures generated for the test suite. They contain no third-party material.
