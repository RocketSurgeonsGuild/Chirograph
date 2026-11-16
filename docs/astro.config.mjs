import { unified } from "@astrojs/markdown-remark";
import starlight from "@astrojs/starlight";
import { defineConfig } from "astro/config";
import starlightAutoDrafts from "starlight-auto-drafts";
import starlightChangelogs from "starlight-changelogs";
import starlightGithubAlerts from "starlight-github-alerts";
import starlightHeadingBadges from "starlight-heading-badges";
import starlightImageZoom from "starlight-image-zoom";
import starlightLinksValidator from "starlight-links-validator";
import starlightLlmsTxt from "starlight-llms-txt";
import starlightPageActions from "starlight-page-actions";
import { starlightIconsPlugin } from "starlight-plugin-icons";
import starlightScrollToTop from "starlight-scroll-to-top";
import starlightSidebarTopics from "starlight-sidebar-topics";
import starlightTags from "starlight-tags";

// In GitHub Actions, the site is served under /Chirograph; locally we run at the root. The gh-pages branch holds latest main at
// that root, each release under /Chirograph/v<version> and pull request previews under /Chirograph/pr-preview/pr-<n>, so CI sets
// DOCS_BASE to the folder a build deploys to. The version switcher reads versions.json from the root.
const root = process.env.DOCS_ROOT || (process.env.GITHUB_ACTIONS ? "/Chirograph" : "");
const base = process.env.DOCS_BASE || root;

export default defineConfig({
    site: "https://rocketsurgeonsguild.github.io",
    base,
    vite: {
        define: {
            __DOCS_ROOT__: JSON.stringify(root),
        },
    },
    markdown: {
        processor: unified(),
    },
    integrations: [
        starlight({
            title: "Chirograph",
            description: "Typed HTTP clients generated from OpenAPI documents at build time.",
            logo: {
                light: "./src/assets/chirograph-wordmark-light.svg",
                dark: "./src/assets/chirograph-wordmark-dark.svg",
                alt: "Chirograph",
                replacesTitle: true,
            },
            social: [
                {
                    icon: "github",
                    label: "GitHub",
                    href: "https://github.com/RocketSurgeonsGuild/Chirograph",
                },
            ],
            components: {
                // Adds the docs version switcher (src/components/VersionSelect.astro) next to the theme picker.
                ThemeSelect: "./src/components/overrides/ThemeSelect.astro",
            },
            customCss: ["./src/styles/theme.css"],
            plugins: [
                starlightAutoDrafts(),
                starlightGithubAlerts(),
                starlightSidebarTopics(
                    [
                        {
                            label: "Getting Started",
                            link: "/guides/",
                            icon: "open-book",
                            items: [{ autogenerate: { directory: "guides" } }],
                        },
                        {
                            label: "Concepts",
                            link: "/concepts/",
                            icon: "information",
                            items: [{ autogenerate: { directory: "concepts" } }],
                        },
                        {
                            label: "Reference",
                            link: "/reference/",
                            icon: "document",
                            items: [{ autogenerate: { directory: "reference" } }],
                        },
                        {
                            label: "Changelog",
                            link: "/changelog/",
                            icon: "list-format",
                        },
                    ],
                    {
                        exclude: ["/tags/**", "/changelog/**"],
                    },
                ),
                starlightLinksValidator({
                    errorOnRelativeLinks: false,
                }),
                starlightHeadingBadges(),
                starlightImageZoom(),
                starlightScrollToTop(),
                starlightPageActions({
                    editLink: "https://github.com/RocketSurgeonsGuild/Chirograph/edit/main/docs/src/content/docs/",
                }),
                starlightIconsPlugin(),
                starlightTags(),
                starlightChangelogs(),
                starlightLlmsTxt(),
            ],
        }),
    ],
});
