import { defineCollection } from "astro:content";
import { docsLoader } from "@astrojs/starlight/loaders";
import { docsSchema } from "@astrojs/starlight/schema";
import { changelogsLoader } from "starlight-changelogs/loader";
import { starlightTagsExtension } from "starlight-tags/schema";

export const collections = {
    docs: defineCollection({
        loader: docsLoader(),
        schema: docsSchema({ extend: starlightTagsExtension }),
    }),
    changelogs: defineCollection({
        loader: changelogsLoader([
            {
                provider: "github",
                base: "changelog",
                owner: "RocketSurgeonsGuild",
                repo: "Chirograph",
                // Unauthenticated, the release list counts against GitHub's 60 requests an hour per IP, shared by every job on
                // the runner's address: the docs build then fails with a 403 now and then, and takes the CI build with it. The
                // build workflow already passes the job's GITHUB_TOKEN.
                token: import.meta.env.GH_API_TOKEN ?? process.env.GITHUB_TOKEN,
            },
        ]),
    }),
};
