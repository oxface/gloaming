# Gloaming

The project's vocabulary for reusable capabilities, the applications that adopt them,
and the identity of work performed in those applications.

## Language

**Gloaming**:
The independently adoptable .NET libraries and the repository that develops them.
Wholesale is the reference application.

**Library segment**:
A selected reusable capability presented to a consumer through its documented contract.
_Avoid_: Application framework

**Application patterns**:
Documents describing a preferred consumer-owned application structure and implementation
tasks, adapted to the target repository and selected capabilities.
_Avoid_: Application runtime

**Reference sample**:
A working application used to prove behavior and demonstrate how selected capabilities
compose together.
_Avoid_: Complete product

**Consumer**:
An application adopting selected library capabilities and its own application structure.
_Avoid_: Sample, business customer

**EventReactions**:
The Gloaming library family for local event capture and reactions, independent of an application's
domain model.
_Avoid_: DDD foundation, Reactive Streams

**Access model**:
The product's model of users, Organizations, memberships, invitations, roles and permissions.
_Avoid_: Execution context, authentication provider

**Dead letter**:
A retained inbox delivery or outbox message excluded from automatic selection by the
consumer's failure policy. Authorized operator retry makes the same retained work eligible
again, preserving identity and payload. Outbox dead-letter state cannot retract external
acceptance or cancel an obsolete publisher already in flight. The earlier inbox implementation
called this state quarantine.

**Inbox receipt**:
The receiver's retained evidence for one delivery in a subscription and producer namespace.
Deleting a processed receipt ends its delivery-deduplication evidence; an old delivery may
then be received and processed again.

**Receipt generation**:
One lifetime of an inbox receipt, identified by ReceiptId. A duplicate or operator retry
keeps that generation; receiving the delivery after receipt deletion creates another.
_Avoid_: Message identity, operator authority

**Command receipt**:
Retained evidence for a consumer-selected logical action and its original business result.
Different deliveries may represent that same action. Equal retained attempts recover the result;
removing the evidence ends its duplicate protection. The receipt grants no authority.
_Avoid_: Inbox receipt, entity identity, current business state

**Application user**:
A person represented by a stable product identity, independently of their login provider
or tenant memberships.
_Avoid_: External identity, actor

**External identity**:
A login account at an authentication provider, associated with an application user.
_Avoid_: Application user, email address

**Tenant**:
The boundary within which an application's data and operations are isolated.
_Avoid_: Organization, membership

**Organization**:
The sample's business grouping of users, memberships and resources, corresponding to a
technical tenant boundary in this sample.
_Avoid_: Universal tenant model, authentication-provider tenant

**Membership**:
An application user's relationship to an Organization; one user can belong to multiple
Organizations.
_Avoid_: Actor identity, tenant selection, permission

**Tenantless execution**:
Work deliberately performed outside a tenant boundary.
_Avoid_: Missing tenant, unrestricted access

**Actor**:
The human or system identity performing the current operation, or its explicit anonymous
designation when no such identity is established.
_Avoid_: Initiator, authorized user, actor-model component

**Anonymous execution**:
Work deliberately performed without an identified human or system actor.
_Avoid_: Missing actor, tenantless execution

**Initiator**:
The identity that originally caused work to begin, which may differ from its current actor.
_Avoid_: Executing actor, authority
