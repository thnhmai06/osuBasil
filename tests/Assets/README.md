# Test assets

Real files that tests read; a test never copies their content into its own source.

| Path                                             | What it is                                                    | Used for                                   |
|--------------------------------------------------|---------------------------------------------------------------|--------------------------------------------|
| `Beatmaps/*.osu`                                 | One real difficulty per mode                                  | analyser tests                             |
| `Sets/padoru-standard.osz`                       | Real standard set, 26 difficulties                            | reader, analyser                           |
| `Sets/hopes-and-dreams-catch.osz`                | Real catch set, 2 difficulties, audio and background          | reader                                     |
| `Sets/mosi-mosi-taiko.osz`                       | Real taiko set, 5 difficulties; audio and video left out      | reader (missing audio)                     |
| `Sets/selene-ui-mania.osz`                       | Real mania set, one difficulty; audio and background left out | reader                                     |
| `Images/avatar-default.png`                      | 512x512 RGBA PNG                                              | screenshot inspector                       |
| `Images/background-1920x1200.jpg`                | JPEG with EXIF data                                           | screenshot inspector                       |
| `Images/seasonal-3500x2487-header.png`           | Interlaced PNG cut off before its pixel data                  | inspector reads dimensions from the header |
| `Audio/preview-source.mp3`, `Audio/hitsound.ogg` | mp3 and Ogg Vorbis audio                                      | preview building                           |
| `Faqs/welcome.txt`                               | A FAQ entry                                                   | FAQ storage                                |

Keep this folder small: leave out audio, video and storyboards a test does not read.
