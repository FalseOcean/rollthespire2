# Why Can the Future Be Predicted?

You left Fake Merchant without buying a thing. Why might the next shop stock different items? Fake Merchant picks its stock with the event's own randomness, then uses `Shops` to set prices. By the time you've seen those prices, the stream the next regular shop will use has already moved forward.

Much of the Spire's randomness starts from a seed. To work out what an action will give you, you need to know which stream it draws from, where that stream has reached, and what it can pick. Seed search works through one seed after another, replaying the part you care about to see whether it gives you what you want. To see how the seed text becomes a starting point, read [[seed-history|Seeds: What Are We Searching?]].

A regular shop advances the shop stream when it generates cards and rolls ordinary relic rarities. Combat reward generation uses `Rewards`. By the time you can see a reward, those draws have already happened. Skip the reward and the next one still picks up where that stream left off.

If you use New Leaf and later open Pandora's Box, the Box continues from your own `Transformations` stream. A transform at Morphic Grove uses the event's own stream. For Pandora's Box, you also need to know how many removable Basic Strikes and Defends are left in your deck. That determines how many cards the Box transforms, even when the random starting point is the same.

## How many draws does a shuffle use?

When an effect such as War Paint shuffles its candidates, it takes one number from the original stream and uses it to start a temporary stream for the shuffle. Add a candidate and the shuffle may pick something different. The original stream still advances one step. When Bones draws its final curse, it picks up from the same personal stream War Paint used. See [[neow-bones-curse|the final Curse from Bones]] for what to specify when filtering.

For results farther into the run, you may also need to know which route you'll take, how many fights you'll have, or what you'll choose at an event. Once you can work out a result for those choices, there's still the question of how to express what you want in the search. See [[events|Events]] and [[event-transform-results|Event transforms]] for examples.
