# API client contracts

Every host this project sends a request to publishes, or measurably
enforces, rules about how a client should behave. This page records those
rules per host, cites where each one comes from, and says whether this
repository follows it and in which file.

Two kinds of claim appear below.

- **Published.** The rule is written down by the party that runs the
  service. The citation is the URL of that page.
- **Measured.** The rule is not published anywhere this project can cite,
  but the service states it in its own responses. The citation is the
  request that was sent and the date it was sent, so anyone can repeat it.

Nothing here is inferred from a failure. A rule that could not be sourced
is left out rather than guessed at.

## Who contacts what

| Host | Reached by | Whose client |
| --- | --- | --- |
| `api.guildwars2.com` | the running module, for recipes, prices, items, currencies and the build id | ours (`Module.cs` owns the `HttpClient`) |
| `api.guildwars2.com` | the running module, for account data | Blish HUD's, through `Gw2ApiManager` |
| `api.guildwars2.com` | `tools/TaimisToolbench.RecipeSeeder`, `tools/VendorOfferUpdater` | ours |
| `wiki.guildwars2.com/api.php` | `tools/VendorOfferUpdater`, `tools/MysticForgeSeeder` | ours |
| `render.guildwars2.com` | the running module, for item and currency icons | Blish HUD's content service |
| `raw.githubusercontent.com` | `tools/build-glyph-font.py --fetch`, at development time only | ours |

The running module never contacts the GW2 Wiki or gw2efficiency. Wiki data
reaches players as committed seed files under `ref/`, written offline by
the tools. `Services/WikiLinkBuilder.cs` composes wiki article URLs and
`Services/WikiLinkLauncher.cs` hands one to the operating system's browser
on a click; neither issues a request from this process.

---

## 1. The GW2 Wiki: MediaWiki Action API and Semantic MediaWiki

`https://wiki.guildwars2.com/api.php` is a MediaWiki installation with the
Semantic MediaWiki extension. It is run by ArenaNet, not by the Wikimedia
Foundation, so Wikimedia's own operational policies do not govern it. What
does apply is MediaWiki's published guidance for clients of the Action API,
which is the software this wiki runs.

### What the contract asks

| Rule | What the source says | Source |
| --- | --- | --- |
| Identify yourself | "Set an informative User-Agent string with contact information, or you may be IP-blocked without notice." | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| User-Agent format | `clientname/version (contact information e.g. username, email) framework/version`. "Do not simply copy the user-agent of a popular web browser." | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| Contact address | Given "as an email address, a website, or a wiki user". Generic agents such as "curl", "lwp", "Python-urllib" may be blocked. Example: `CoolBot/0.0 (https://example.org/coolbot/; coolbot@example.org) generic-library/0.0`. Parts that do not apply may be omitted. | [Wikimedia Foundation User-Agent Policy](https://foundation.wikimedia.org/wiki/Policy:Wikimedia_Foundation_User-Agent_Policy) |
| Serialise requests | "There is no hard speed limit on read requests, but be considerate and try not to take a site down." "Making your requests in series rather than in parallel, by waiting for one request to finish before sending a new request, should result in a safe request rate." | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| Ask for many things at once | Use the pipe character, `titles=PageA\|PageB\|PageC`, instead of one request per title, and use a generator instead of a request per result. | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| Compress | "Use GZip compression when making API calls by setting `Accept-Encoding: gzip` to reduce bandwidth usage." | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| `maxlag` | "If your task is not interactive, i.e. a user is not waiting for the result, you should use the `maxlag` parameter." Recommended value for a non-aggressive client is `maxlag=5`. | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette), [Manual:Maxlag parameter](https://www.mediawiki.org/wiki/Manual:Maxlag_parameter) |
| Reading a lag refusal | The refusal arrives with a 200 status code and an `error` object whose `code` is `maxlag`, plus a `Retry-After` header and an `X-Database-Lag` header. "If you get a lag error, pause your script for at least 5 seconds before trying again. Be careful not to go into a busy loop." | [Manual:Maxlag parameter](https://www.mediawiki.org/wiki/Manual:Maxlag_parameter) |
| Telling a lag refusal from a cache timeout | A caching layer may also answer 503. `X-Database-Lag` is distinctive to replication lag; Varnish errors carry no `Retry-After`; a lag body matches `/Waiting for [^ ]*: [0-9.-]+ seconds? lagged/`. "Repeating the operation on timeout would use excessive server resources and may leave your client in an infinite loop." | [Manual:Maxlag parameter](https://www.mediawiki.org/wiki/Manual:Maxlag_parameter) |
| Rate-limit errors | A rate-limited request returns the error code `ratelimited`. "When you encounter this error, you may retry that request, however you should increase the time between subsequent requests. A common strategy for this is Exponential backoff." | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| Cache | "If your requests obtain data that can be cached for a while, you should take steps to cache it, so you don't request the same data over and over again." | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| Prefer GET for reads | POST responses are not cacheable and in a multi-datacenter configuration may travel to a farther data center. Where a read must use POST, "consider setting the `Promise-Non-Write-API-Action: true` header". | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |
| Output format | "All new API users should use JSON." | [API:Etiquette](https://www.mediawiki.org/wiki/API:Etiquette) |

An error response is not an HTTP failure. A lag refusal, at least,
arrives with a 200 status code and carries its `error` object in the
body, so a client that branches on status alone cannot see one.
([Manual:Maxlag parameter](https://www.mediawiki.org/wiki/Manual:Maxlag_parameter))

### Semantic MediaWiki query limits

Both wiki tools use `action=ask`. Semantic MediaWiki ships these defaults,
which a wiki can override; the values in force are discoverable from the
error an over-large query returns.

| Setting | Default | What it bounds |
| --- | --- | --- |
| `QDefaultLimit` | 50 | rows returned when a query names no limit |
| `QMaxInlineLimit` | 500 | rows a single query may print |
| `QUpperbound` | 5000 | rows printable once an offset is applied |
| `QMaxLimit` | 10000 | rows ever retrieved |
| `QMaxSize` | 16 | conditions permitted in one query |

Source: the `config_prefix`/`config` block of
[Semantic MediaWiki's `extension.json`](https://github.com/SemanticMediaWiki/SemanticMediaWiki/blob/master/extension.json).
An offset ceiling of this shape is why
`tools/VendorOfferUpdater/WikiSmwClient.cs` splits a large result set by
vendor-name prefix rather than paging past it.

### The `robots.txt` entry on `/api.php`

<https://wiki.guildwars2.com/robots.txt> reads, in full apart from a
comment line and three further localised spellings of the last path:

```
User-agent: *
Disallow: /index.php
Disallow: /api.php
Disallow: /load.php
Disallow: /wiki/Special:*
```

`/api.php` is the endpoint `tools/VendorOfferUpdater/WikiSmwClient.cs` and
`tools/MysticForgeSeeder/WikiRecipeClient.cs` send every request to.

Two readings are available:

- The Robots Exclusion Protocol applies to "automatic clients"
  ([RFC 9309 section 1](https://www.rfc-editor.org/rfc/rfc9309.html#section-1)),
  which a scraper is. Read that way, both tools are disallowed outright.
- A MediaWiki install commonly disallows `/api.php` and `/index.php` to
  keep search engines out of dynamic and duplicate content, while still
  publishing the Action API for clients to use. Read that way the line is
  about indexing, not about API access.

The second reading is the one this project acts on, and the tools continue
to use the endpoint. The decision, its reasoning, the obligations taken on
with it and what would reopen it are recorded in
[`DECISIONS.md`](DECISIONS.md) under "Stopping wiki API access over the
`robots.txt` entry". The rules in this section are those obligations; the
compliance table below is where each one stands.

### Where this repository stands

| Rule | Status | Evidence |
| --- | --- | --- |
| User-Agent set | Met | `tools/VendorOfferUpdater/Program.cs`, `tools/MysticForgeSeeder/Program.cs` both set one before handing the client to their scraper |
| User-Agent carries a contact address | Met in `tools/MysticForgeSeeder/Program.cs` | the agent names the repository that publishes the tool |
| Requests are serial | Met | `tools/VendorOfferUpdater/WikiSmwClient.cs` and `tools/MysticForgeSeeder/WikiRecipeClient.cs` both await one response before sending the next, and both sleep between requests |
| Requests ask for many rows at once | Met | `tools/MysticForgeSeeder/WikiRecipeClient.cs` asks for 500 rows a page and resolves item ids in batches of 10 |
| `Accept-Encoding: gzip` | Not met | neither tool configures decompression, so every response arrives uncompressed |
| `maxlag` | Not met in `tools/MysticForgeSeeder/WikiRecipeClient.cs` | no query carries the parameter |
| A refusal is read as a refusal | Not met in `tools/MysticForgeSeeder/WikiRecipeClient.cs` | the pagination loop treats a body with no `query.results` as the end of the results and stops, so an `error` body ends the scrape early and silently |
| `Retry-After` honoured | Partly met | both clients read the header's delta form only, so a `Retry-After` sent as an HTTP-date is treated as absent |
| Exponential backoff | Met | both clients double a base delay per attempt and add jitter |
| Caching | Met | `tools/MysticForgeSeeder/Program.cs` reads and writes `ref/mf_item_id_cache.json` and re-resolves a name only under `--force-resolve` |
| GET preferred for reads, or `Promise-Non-Write-API-Action` on a POST read | Not met in `tools/MysticForgeSeeder/WikiRecipeClient.cs` | queries are POSTed to keep the URL short, and the header is not sent |
| JSON output | Met | every query sets `format=json` |

---

## 2. The official GW2 API

`https://api.guildwars2.com` publishes its own limits in its responses.
The values below were measured on 2026-09-05 by sending the request named
in each row; `Quaggans` is the server name the API answers with.

| Rule | Measured value | Request |
| --- | --- | --- |
| Requests per minute | `X-Rate-Limit-Limit: 600` on every response, and the API names the header in `Access-Control-Expose-Headers` | `GET /v2/build` |
| Ids per request | 200. A 201-id list is refused with HTTP 400 and the body `id list too long; this endpoint is limited to 200 ids at once` | `GET /v2/items?ids=1..201` |
| Partially resolvable id list | HTTP 206 with `X-Result-Count` naming how many of the requested ids came back | `GET /v2/items?ids=1..200` returned 206 and `X-Result-Count: 150` |
| Cacheability | `Cache-Control: public,max-age=3600` on item data, `Cache-Control: private,max-age=3600` on the build id, with a matching `Expires` | `GET /v2/items?ids=...`, `GET /v2/build` |
| Compression | supported: `Accept-Encoding: gzip` returns `Content-Encoding: gzip` and `Vary: Accept-Encoding`. The same three-item response was 1,460 bytes uncompressed and 575 bytes compressed | `GET /v2/items?ids=19721,19976,24295`, sent twice, once with `Accept-Encoding: identity` and once with `gzip` |
| Authentication | an authenticated endpoint without a token answers HTTP 401 with the body `Invalid access token` | `GET /v2/account` |
| Schema version | the `v=` parameter pins the response shape; `Services/Gw2RecipeApiClient.cs` and `tools/TaimisToolbench.RecipeSeeder/Program.cs` each pin a literal date | |

### Terms of use

ArenaNet documents the API on the GW2 Wiki at
<https://wiki.guildwars2.com/wiki/API:Main>, which transcludes
[API:Terms of Use](https://wiki.guildwars2.com/wiki/API:Terms_of_Use):

> Any use of the APIs must comply with ArenaNet's Content Terms of Use and
> Website Terms of Use. Use of the APIs constitutes acceptance of the terms
> and conditions contained in the ArenaNet Content Terms of Use, Website
> Terms of Use, and any related terms or conditions when they are posted.

The two documents it names are
<https://www.guildwars2.com/en/legal/guild-wars-2-content-terms-of-use/>
and <https://www.guildwars2.com/en/legal/website-terms-of-use/>.

No User-Agent requirement is published for this host, and no rate limit is
published outside the `X-Rate-Limit-Limit` header. Sending a User-Agent is
still the only way ArenaNet can tell this client's traffic apart from
anyone else's, or reach whoever is generating it, which is why the module
and the seeders send one.

`API:Main` also states that authentication goes in an
`Authorization: Bearer` header or an `access_token` query parameter, and
that the schema version goes in the `v=` query parameter. Its
"Cache Validation" section, which would cover `Last-Modified` and
`If-Modified-Since`, is marked as a stub and carries no guidance.

### Where this repository stands

| Rule | Status | Evidence |
| --- | --- | --- |
| Client identifies itself | Met | `Services/Gw2ApiUserAgent.cs` builds the agent and `Module.cs` applies it to the single `HttpClient` every runtime API call shares; `tools/TaimisToolbench.RecipeSeeder/Program.cs` applies the same helper |
| 200 ids per request | Met | `Services/Gw2RecipeApiClient.cs` batches at 200, `Services/Gw2AccountSnapshotService.cs` chunks item lookups at 200, `tools/TaimisToolbench.RecipeSeeder/Program.cs` batches at 200 |
| Stay inside 600 requests a minute | Met in practice, not enforced | the heaviest runtime walk, `Services/Recipes/RecipeCorpusRefresher.cs`, sleeps one second between 200-id batches; `Services/RecipeService.cs` fans out at concurrency 4 with no pacing, but is bounded by how many recipes a plan misses in the committed corpus |
| Honour `Retry-After` | Partly met | `tools/TaimisToolbench.RecipeSeeder/HttpRetry.cs` reads both the delta and the date form; `Services/Gw2BuildApiClient.cs` waits a fixed two seconds and reads neither |
| Distinguish "come back later" from "this request is wrong" | Partly met | `tools/TaimisToolbench.RecipeSeeder/HttpRetry.cs` retries 429 and 5xx only; `Services/Gw2BuildApiClient.cs` retries any failure |
| A refusal must not read as an empty result | Met in the seeder | `tools/TaimisToolbench.RecipeSeeder/Program.cs` throws once a batch is unrecoverable instead of returning an empty batch into the seed |
| Cache | Met | the recipe corpus persists across sessions in `Services/Recipes/OverlayRecipeCacheStore.cs`; prices carry a 15-minute TTL in `Services/TradingPostService.cs`; item metadata is memoised for the session in `Services/ItemMetadataService.cs`; `Services/CurrencyMetadataService.cs` fetches `ids=all` once |
| Compression | Not met | the module's `HttpClient` uses the default handler, which requests no encoding |

---

## 3. The render service

[API:Render service](https://wiki.guildwars2.com/wiki/API:Render_service)
states the contract: a URL has the form
`https://render.guildwars2.com/file/{signature}/{file_id}.{format}`, both
the signature and the file id are required, and the only valid formats are
`png` and `jpg`. A v2 endpoint returns the full URL rather than the parts.

Item and currency icon URLs therefore arrive from the GW2 API as data. This
repository never composes one. `Views/Rendering/IconControls.cs` hands the
URL it was given to Blish HUD's content service, which performs the fetch
and holds the result.

Measured on 2026-09-05, `GET https://render.guildwars2.com/file/...png`:

| Property | Value |
| --- | --- |
| Cacheability | `Cache-Control: public,max-age=604800` (seven days) |
| Delivery | CloudFront, reporting `X-Cache: Hit from cloudfront` |

The seven-day lifetime is why a texture is worth fetching once and holding.
The holding is Blish HUD's; what this repository controls is how many
distinct URLs it asks for. An icon row with no URL never reaches the
content service at all.

---

## 4. What Blish HUD controls, and what is left to us

Account data is fetched through Blish HUD's `Gw2ApiManager`, which owns the
underlying Gw2Sharp client. The User-Agent it sends, its connection reuse,
its timeouts and any retry it performs are Blish HUD's, not this
repository's, and cannot be set from module code.

Two files use it: `Services/Gw2AccountSnapshotService.cs` and
`Services/Gw2AccountRecipeClient.cs`.

What remains ours on that path is request volume and what we keep:

- A snapshot costs five fixed account calls, two calls per character, one
  call per 200 uncached items, and one currency call if the currency cache
  is empty. Character calls run two at a time and characters run one after
  another (`Services/Gw2AccountSnapshotService.cs`).
- Item names and currency metadata are held across refreshes, so a second
  snapshot on the same session re-fetches neither
  (`Services/Gw2AccountSnapshotService.cs`).
- The learned-recipe list is fronted by a five-minute cache with explicit
  invalidation when the subtoken changes
  (`Services/CachingAccountRecipeClient.cs`).
- A single refresh is claimed once and cannot overlap itself
  (`Services/SnapshotRefreshSlot.cs`).

Every other GW2 API call the module makes goes through the `HttpClient`
built in `Module.cs`, which is entirely ours to configure.

---

## 5. The licence on scraped wiki content

`ref/vendor_offers.json` and `ref/mystic_forge_recipes.json` are built from
GW2 Wiki pages and ship inside the module.
[Guild Wars 2 Wiki:Copyrights](https://wiki.guildwars2.com/wiki/Guild_Wars_2_Wiki:Copyrights)
divides the wiki's content in two:

> Content provided by individual contributors, which is original and does
> not infringe upon the intellectual property rights of any third party, is
> available under the GNU Free Documentation License 1.3 (GFDL).

> Content obtained from Guild Wars 2, its web sites, manuals and guides,
> concept art and renderings, press and fansite kits, and other such
> copyrighted material, may also be available from this site. All rights,
> title and interest in and to such content remains with ArenaNet or
> NCsoft, as applicable, and such content is not licensed pursuant to the
> GFDL.

The seeds hold facts taken from the first category: which vendor sells
which item, at what price, in what currency. No wiki prose, image or page
text is copied into them. Whether a compilation of those facts carries the
GFDL's attribution requirement is a question this repository has not
answered anywhere, and this page does not answer it either.

---

## 6. raw.githubusercontent.com

`tools/build-glyph-font.py` fetches Bootstrap icon SVGs one at a time, with
a thirty second timeout, and only when `--fetch` is passed. It is a
development-time tool; nothing in the shipped module reaches this host.
GitHub's rate limits for unauthenticated raw content are documented at
<https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api>.

---

## Open breaches and open questions

These are known, sourced above, and not settled.

1. **No `maxlag` in `tools/MysticForgeSeeder/WikiRecipeClient.cs`.** Adding
   the parameter alone would make the tool worse, not better: a lag refusal
   arrives as HTTP 200 with an `error` body, and the pagination loop reads a
   body with no `query.results` as "no more results" and stops. The
   parameter is safe to add only once that loop tells a refusal from an
   empty page.
2. **`Retry-After` is read only in its delta form.** RFC 9110 section
   10.2.3 allows an HTTP-date, and both wiki clients treat a dated header as
   absent. `tools/TaimisToolbench.RecipeSeeder/HttpRetry.cs` reads both.
3. **No request compression anywhere.** The GW2 API supports gzip, measured
   above; MediaWiki asks for it. No client in this repository sets
   `Accept-Encoding`.
4. **`Services/Gw2BuildApiClient.cs` retries any failure on a fixed delay.**
   It repeats a request the API has already rejected as malformed, and it
   ignores a `Retry-After` the API sends.
5. **POST reads carry no `Promise-Non-Write-API-Action` header.**
   `tools/MysticForgeSeeder/WikiRecipeClient.cs` POSTs its `action=ask`
   queries to keep URLs short, which is allowed, but does not send the
   header that tells MediaWiki the request is a read.
6. **The GFDL attribution question on scraped facts.** Section 5 states
   what the wiki's copyright page says and what the seeds actually hold.
   Nothing in the repository states a position on it.

The `robots.txt` entry on `/api.php` is no longer among these. Section 1
records what it says, and [`DECISIONS.md`](DECISIONS.md) records the
decision taken on it.
