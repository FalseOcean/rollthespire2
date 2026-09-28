# Event Appearance: What Will a Question-Mark Room Contain?

> Applies to: **Slay the Spire 2 Beta 0.111.0**

## What You Want

Seeing a question mark on the map, you may care less about an event's queue position than:

> **If I enter this room, will I meet the event I want?**

Two things must happen in sequence:

> **The question mark must become an Event room.**
> **The next legal event in the current queue must be the target.**

**A question mark is not necessarily an Event, and an Event room does not unconditionally take the next queue entry.**

# Step 1: Does the Question Mark Become an Event?

A `?` is an Unknown room. On entry, the game determines whether it becomes a Monster, Treasure, Shop, or Event room.

Normal base probabilities at the start of an Act are:

> Monster: **10%**
> Treasure: **2%**
> Shop: **3%**
> Event: the remainder, about **85%**

Elites normally do not participate. The first question mark is likely, but not guaranteed, to become an Event.

# Probabilities Change with Earlier Outcomes

These probabilities do not reset after every question mark. A non-Event type that does not appear gradually becomes more likely.

If a room becomes an Event, the Monster, Treasure, and Shop chances increase. If the next room becomes a Monster, its chance resets to base while other permitted types keep accumulating.

**The second question mark is affected by the first one's result.**

A new Act resets the probabilities, but not the random sequence itself. Act 2's results still depend on how many question marks were actually entered earlier.

# Map Position Can Affect the Result

Shops have additional restrictions. The game blocks a question mark from becoming a Shop if the player just left a Shop or all outgoing nodes are fixed Shops.

This changes the current room and how probabilities accumulate afterward.

Relics, cards, Modifiers, and special mechanisms can also change which room types are allowed.

Whether a particular question mark is an Event therefore depends on **seed, previous Unknown-room history, current probabilities, and route context**. The presence of a `?` icon alone is insufficient.

# Step 2: Select an Event Only After Rolling an Event Room

Once the room is classified as Event, the game reads the current Act's pre-shuffled event queue.

It does not simply use the first item unconditionally. It checks candidates in order from the current event position.

# Eligibility Is Checked Again on Arrival

The game asks whether the candidate can appear **now**.

Some conditions, such as Act, mode, and unlock state, are known at the opening. Others depend on gold, health, deck, relics, potions, Event Pets, floor, or other current state.

An ineligible candidate does not trigger a new shuffle. The game **skips it and checks the next candidate**, without an additional random roll for the skip itself.

For a queue A → B → C → D, if A and B are currently ineligible, the player may encounter C directly.

# Previously Visited Events Are Also Skipped

The game records **which events have actually been visited throughout the run**. If a previously visited identity appears again, it is normally skipped.

That record persists **across Acts**. Appearance depends both on the current Act's queue and on actual events encountered in earlier Acts.

# Can an Act 1 Event Reappear in Act 2?

Several cases must be distinguished.

## Act 1-Only Events

An event belonging only to Act 1's local pool is simply absent from Act 2's queue. Acts 2 and 3 also have their own local pools.

## Shared Events

Shared events can be candidates in multiple Acts. The same identity can occur in Act 1's queue and Act 2's separately shuffled queue.

The key question is **whether it was actually encountered**.

## Actually Visited in Act 1

A shared event that occurs in Act 1 enters the run's visited-event record. Act 2 normally skips it even if its queue contains that identity.

**A shared event therefore normally does not actually repeat across Acts.**

## Queued in Act 1, but Never Visited

An event may be in Act 1's raw queue without being reached, or be skipped for failing its conditions. It has not been recorded as visited.

If allowed in Act 2, **it can still be encountered there**. Being present in Act 1's queue and being visited in Act 1 are different facts.

# Act 2 Does Not Continue Act 1's Leftover Queue

World generation establishes separate orders for Acts 1, 2, and 3. Shared events enter each appropriate pool and participate in its shuffle.

**What is shared is eligibility as a candidate, not one event queue across all Acts.** The visited-event record is what carries forward.

# “Shared” Does Not Mean Allowed in Every Act

A shared event is outside any one Act's local event collection, but can still impose its own Act restriction.

Some allow Acts 1 / 2, some only Act 2, and some Acts 2 / 3. To know whether an Act 1 candidate can appear later, first check whether it permits Act 2 at all.

# An Easily Missed Event Position

Ancient rooms are also Event-type rooms. Entering the Ancient at an Act's start normally advances the event position once, even though it does not take an ordinary event from the queue.

**The first actual question-mark Event is therefore not simply the first item in the shuffled queue.** A shuffle list alone is insufficient; the current event position matters too.

# Events Can Repeat in an Extreme Case

Normally, visited events are skipped. But if all distinct candidates in the current queue fail to provide a new legal event, the game stops searching and permits repetition.

Avoiding repeats is therefore not an absolute rule. More precisely, **the game prefers unvisited candidates while suitable ones remain**. This extreme case rarely matters on ordinary route lengths.

# What Must Happen for a Specific Event to Appear?

For event X at a particular question mark:

> The room must roll Event, using Unknown randomness, current probabilities, and route context.
> The Act's event position must reach X's part of the queue.
> Earlier candidates may be skipped for ineligibility or prior visits.

X itself must be allowed in the Act, satisfy its current-state conditions, normally remain unvisited, and avoid replacement by another special mechanism.

Only then does **that question mark actually become event X**.

# What Does RT2 Do Today?

Current event filtering does not claim to simulate this whole route history.

It restores the original seeded order and applies static filtering using facts known before search, such as an incompatible Act, mode, or unlock state.

For conditions that require arrival-time gold, health, deck, or Event Pet ownership, RT2 does not invent an answer. Such candidates remain.

The resulting queue means **candidate order still possible under known conditions**, not a complete proof of what every future question mark contains.

For single-player, non-tutorial runs without special room effects, the map side can predict Event / Monster / Treasure / Shop on an explicit route. Later Acts additionally require earlier Act routes to establish prior Unknown RNG consumption.

Even then, RT2 does not equate the Nth Event room to the Nth event candidate, because **dynamic arrival-time eligibility** remains between them.

To guarantee a particular event at a particular route node, room outcomes, event queues, visited-event records, and player-state changes must form one complete history.

Current event filtering does not pretend that integration is already complete.
