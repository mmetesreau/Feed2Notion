#r "nuget: FSharp.Data"
#r "nuget: FsHttp"
#r "nuget: FSharp.Control.AsyncSeq"
#r "nuget: Notion.Net"

module Notion =
    open System.Collections.Generic
    open Notion.Client

    let private titlePropertyName = "Title"
    let private linkPropertyName = "Link"

    let private pageParams (databaseId: string) =
        PagesCreateParametersBuilder.Create(DatabaseParentInput(DatabaseId = databaseId))

    let private addTitle (title: string) (page: PagesCreateParametersBuilder) =
        page.AddProperty(
            titlePropertyName,
            TitlePropertyValue(
                Title =
                    List<RichTextBase>(
                        [ RichTextText(Text = new Text(Content = title)) :> RichTextBase ]
                        |> Seq.ofList
                    )
            )
        )

    let private addLink (link: string) (page: PagesCreateParametersBuilder) =
        page.AddProperty(linkPropertyName, UrlPropertyValue(Url = link))

    let notExists (client: NotionClient) (databaseId: string) (link: string) =
        async {
            let! searchPageResult =
                client.Databases.QueryAsync(
                    databaseId,
                    DatabasesQueryParameters(Filter = URLFilter(linkPropertyName, equal = link))
                )
                |> Async.AwaitTask

            return searchPageResult.Results.Count = 0
        }

    let create (client: NotionClient) (databaseId: string) (title: string) (link: string) =
        async {
            let page =
                pageParams databaseId
                |> addTitle title
                |> addLink link
                |> fun x -> x.Build()

            try
                let! _ = client.Pages.CreateAsync(page) |> Async.AwaitTask
                return $"✅ {title} ({link})"
            with
            | e -> return $"❌ {e.Message}: {title} ({link})"
        }

    let client (authToken: string) =
        NotionClientFactory.Create(ClientOptions(AuthToken = authToken))

module Feed =
    open System
    open FSharp.Control
    open FSharp.Data

    [<Literal>]
    let private ResolutionFolder = __SOURCE_DIRECTORY__

    type private Types = XmlProvider<"feed.sample", ResolutionFolder=ResolutionFolder, SampleIsList=true>

    type Url = string

    type Entry =
        { Title: string
          Link: string
          Updated: DateTime }

    let downloadEntries (onOrAfter: DateTime) (url: Url) =
        asyncSeq {
            try
                let! feedProvider = Types.AsyncLoad(url)

                match feedProvider with
                | x when x.Feed.IsSome ->
                    for entry in x.Feed.Value.Entries do
                        if entry.Updated.DateTime >= onOrAfter then
                            yield
                                { Title = entry.Title
                                  Link = entry.Links[0].Href
                                  Updated = entry.Updated.DateTime }
                | x when x.Rss.IsSome ->
                    for item in x.Rss.Value.Channel.Items do
                        if item.PubDate.DateTime >= onOrAfter then
                            yield
                                { Title = item.Title
                                  Link = item.Link
                                  Updated = item.PubDate.DateTime }
                | _ -> printfn $"❌ unknown format: {url}"

            with
            | e -> printfn $"❌ {e.Message}: {url}"
        }

open System
open FSharp.Control

let authToken = Environment.GetEnvironmentVariable("NOTION_API_TOKEN")
let databaseId = Environment.GetEnvironmentVariable("NOTION_FEEDITEMS_DATABASE_ID")

let notionClient = Notion.client authToken

[ Feed.Url "https://thinkbeforecoding.com/feed/atom"
  Feed.Url "https://www.compositional-it.com/news-blog/feed/"
  Feed.Url "https://brandewinder.com/atom.xml"
  Feed.Url "https://sergeytihon.com/feed/atom/"
  Feed.Url "https://codeopinion.com/feed/atom/"
  Feed.Url "https://blog.tunaxor.me/feed.atom"
  Feed.Url "https://blog.ploeh.dk/atom" ]
|> AsyncSeq.ofSeq
|> AsyncSeq.collect (Feed.downloadEntries (DateTime.Now - TimeSpan.FromDays(7.)))
|> AsyncSeq.filterAsync (fun x -> Notion.notExists notionClient databaseId x.Link)
|> AsyncSeq.mapAsync (fun x -> Notion.create notionClient databaseId x.Title x.Link)
|> AsyncSeq.iter (printfn "%A")
|> Async.RunSynchronously
