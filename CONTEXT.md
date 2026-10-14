# Modulith Foundry

The project's vocabulary for reusable capabilities, the applications that adopt them,
and the identity of work performed in those applications.

## Language

**Gloaming**:
The independently adoptable .NET libraries developed in this repository. Modulith Foundry
remains the repository, template and reference-sample identity.

**Umbrine**:
The ERP product to be built on the application template and Gloaming libraries developed
here. Its name derives from Umbral; Umbrine and Gloaming share a darkness theme.

**Library segment**:
A selected reusable capability presented to a consumer through its documented contract.
_Avoid_: Foundry framework

**Template**:
A customizable starting point for a consumer-owned application, assembled from exercised
sample setup.
_Avoid_: Application runtime

**Reference sample**:
A working application used to prove behavior and demonstrate how selected capabilities
compose together.
_Avoid_: Complete product

**Consumer**:
An application adopting a library segment or the template.
_Avoid_: Sample, business customer

**Access model**:
The product's model of users, Organizations, memberships, invitations, roles and permissions.
_Avoid_: Execution context, authentication provider

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
