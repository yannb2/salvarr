# Salvarr

Browse the recycle bins of a Radarr/Sonarr media library, read each deleted
file's media info and MKV tags, and compare it side by side with the file that
replaced it in the library.

Salvarr is read-only. It never moves, changes or deletes anything.

![Recycle bin](docs/home.png)

![Comparison with the library file](docs/compare.png)

## What it does

- **Recycle bin list.** Every video file in the `.recycle` folders of `/movies`
  and `/tv`, with size, when it was deleted, and which library file replaced it.
  Search, filter by library, and sort by name, size or date.
- **File page.** Tracks (codec in Radarr's words, resolution, HDR10, HDR10+, HLG,
  Dolby Vision profile, Atmos, DTS:X, channels, language, track name and flags),
  chapters, attachments, and every MKV global tag (TMDB, IMDB, RELEASE_NAME,
  SOURCE, EDITION and the rest of what `tag_release.py` writes).
- **Compare.** The deleted file and the library file in two columns, with every
  row that differs highlighted: general facts, video, each audio and subtitle
  track by position, tags and chapters. "Only show differences" hides the rest.
- **MediaInfo.** The raw `mediainfo` output or ffprobe JSON of either file.
- **Status.** Libraries, the bins found, and the tool versions.

## How it finds things

**Recycle bins** are folders named `.recycle` (configurable) inside each
library root, up to two levels deep: `/movies/.recycle` and
`/movies/live-action/.recycle` are both found.

**When a file was deleted** is the later of its change time (moving a file
into the bin sets it) and its modification time (Radarr and Sonarr set it).

**The library file** is found in two steps:

1. The movie or show folder. Samba's recycle bin keeps the whole path
   (`.recycle/live-action/Alien (1979)/...`), Radarr and Sonarr keep only the
   title folder (`.recycle/Alien (1979)/...`). Salvarr checks each folder name in
   the bin path against the library's movie and show folders, then falls back to
   the title at the start of the file name (`Alien (1979) - ...`).
2. The file in that folder. For movies, the same file name wins, then the same
   edition (`Title (YYYY) - Edition [...]`), then any other cut. For TV, the same
   `S01E02` anywhere in the show folder. When there are several, the file page
   has a list to pick from.

## Running it

Add the service to the compose file (see [compose.example.yaml](compose.example.yaml)):

```yaml
  salvarr:
    image: ghcr.io/yannb2/salvarr:dev
    container_name: salvarr
    user: 1000:1000
    environment:
      TZ: America/Toronto
    ports:
      - 8184:8184
    volumes:
      - /movies:/movies:ro
      - /tv:/tv:ro
    restart: unless-stopped
```

Then open `http://<host>:8184`. The image is on the GitHub Container Registry:
`dev` follows `main`, `latest` and `0.1.0` come from release tags.

### Settings

Everything has a default, and any setting can be changed with an environment
variable (`__` separates levels, lists are numbered from 0):

| Setting | Default | Environment variable |
| --- | --- | --- |
| Libraries | Movies at `/movies`, TV at `/tv` | `Salvarr__Libraries__0__Path=/movies`, `..._Name`, `..._Kind` (`Movies` or `Series`) |
| Recycle bin folder names | `.recycle` | `Salvarr__RecycleFolderNames__0=.recycle` |
| How deep to look for bins | `2` | `Salvarr__SearchDepth=2` |
| Look for HDR10+ (reads 5 frames) | `true` | `Salvarr__DetectHdr10Plus=false` |
| Port | `8184` | `ASPNETCORE_HTTP_PORTS=8184` |

## Developing

.NET 10, Blazor Server with Bootstrap 5 (the layout follows
[Muxarr](https://github.com/KirovAir/muxarr), so the two look alike).
`ffprobe` and `mediainfo` must be on the PATH.

```bash
dotnet test                                   # unit tests
dotnet run --project src/Salvarr              # http://localhost:8184
```

To point a local run at test folders:

```bash
Salvarr__Libraries__0__Path=~/media/movies Salvarr__Libraries__1__Path=~/media/tv \
  dotnet run --project src/Salvarr
```

| Folder | What is in it |
| --- | --- |
| `src/Salvarr/Media` | Runs ffprobe and mediainfo, turns the JSON into tracks, tags and chapters |
| `src/Salvarr/Library` | Finds the bins, lists the files, matches them to the library |
| `src/Salvarr/Compare` | Builds the side-by-side rows |
| `src/Salvarr/Components` | The Blazor pages and the layout |
| `tests/Salvarr.Tests` | MSTest unit tests, with ffprobe samples in `Samples/` |

## Next

- v0.2: restore a file to where it was (and ask Radarr/Sonarr to rescan), delete
  for good, and empty files older than N days. Needs a read-write mount.

## License

GPLv3, like Muxarr, whose layout and styles Salvarr reuses.
